﻿using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.SQLiteWalletRepository.External
{
    public interface ITransactionsToLists
    {
        bool ProcessTransactions(IEnumerable<Transaction> transactions, HashHeightPair block, uint256 fixedTxId = null, long? blockTime = null);
    }

    public abstract class TransactionsToListsBase : ITransactionsToLists
    {
        private readonly Network network;
        private readonly IScriptAddressReader scriptAddressReader;
        protected readonly IWalletTransactionLookup transactionsOfInterest;
        protected readonly IWalletAddressLookup addressesOfInterest;
        private readonly IDateTimeProvider dateTimeProvider;

        public abstract ITopUpTracker GetTopUpTracker(AddressIdentifier address);
        public abstract void RecordSpend(HashHeightPair block, TxIn txIn, string pubKeyScript, bool isCoinBase, long spendTime, Money totalOut, uint256 spendTxId, int spendIndex);
        public abstract void RecordReceipt(HashHeightPair block, Script pubKeyScript, TxOut txOut, bool isCoinBase, long creationTime, uint256 outputTxId, int outputIndex, bool isChange);

        public TransactionsToListsBase(Network network, IScriptAddressReader scriptAddressReader, IWalletTransactionLookup transactionsOfInterest, IWalletAddressLookup addressesOfInterest, IDateTimeProvider dateTimeProvider)
        {
            this.network = network;
            this.scriptAddressReader = scriptAddressReader;
            this.transactionsOfInterest = transactionsOfInterest;
            this.addressesOfInterest = addressesOfInterest;
            this.dateTimeProvider = dateTimeProvider;
        }

        internal IEnumerable<TxDestination> GetDestinations(Script redeemScript)
        {
            ScriptTemplate scriptTemplate = this.network.StandardScriptsRegistry.GetTemplateFromScriptPubKey(redeemScript);

            if (scriptTemplate != null)
            {
                // We need scripts suitable for matching to HDAddress.ScriptPubKey.
                switch (scriptTemplate.Type)
                {
                    case TxOutType.TX_PUBKEYHASH:
                        yield return PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(redeemScript);
                        break;
                    case TxOutType.TX_PUBKEY:
                        yield return PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(redeemScript).Hash;
                        break;
                    case TxOutType.TX_SCRIPTHASH:
                        yield return PayToScriptHashTemplate.Instance.ExtractScriptPubKeyParameters(redeemScript);
                        break;
                    case TxOutType.TX_SEGWIT:
                        TxDestination txDestination = PayToWitTemplate.Instance.ExtractScriptPubKeyParameters(this.network, redeemScript);
                        if (txDestination != null)
                            yield return new KeyId(txDestination.ToBytes());
                        break;
                    default:
                        if (this.scriptAddressReader is ScriptDestinationReader scriptDestinationReader)
                        {
                            foreach (TxDestination destination in scriptDestinationReader.GetDestinationFromScriptPubKey(this.network, redeemScript))
                            {
                                yield return destination;
                            }
                        }
                        else
                        {
                            string address = this.scriptAddressReader.GetAddressFromScriptPubKey(this.network, redeemScript);
                            TxDestination destination = ScriptDestinationReader.GetDestinationForAddress(address, this.network);
                            if (destination != null)
                                yield return destination;
                        }

                        break;
                }
            }
        }

        public bool ProcessTransactions(IEnumerable<Transaction> transactions, HashHeightPair block, uint256 fixedTxId = null, long? blockTime = null)
        {
            bool additions = false;

            // Convert relevant information in the block to information that can be joined to the wallet tables.
            IWalletTransactionLookup transactionsOfInterest = this.transactionsOfInterest;
            IWalletAddressLookup addressesOfInterest = this.addressesOfInterest;

            long currentTime = this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp();

            foreach (Transaction tx in transactions)
            {
                // Build temp.PrevOuts
                uint256 txId = fixedTxId ?? tx.GetHash();
                bool addSpendTx = false;

                for (int i = 0; i < tx.Inputs.Count; i++)
                {
                    TxIn txIn = tx.Inputs[i];

                    if (transactionsOfInterest.Contains(txIn.PrevOut, out HashSet<AddressIdentifier> addresses))
                    {
                        // Record our outputs that are being spent.
                        foreach (AddressIdentifier address in addresses)
                            RecordSpend(block, txIn, address.ScriptPubKey, tx.IsCoinBase | tx.IsCoinStake, blockTime ?? currentTime, tx.TotalOut, txId, i);

                        additions = true;
                        addSpendTx = true;
                    }
                }

                // Build temp.Outputs.
                for (int i = 0; i < tx.Outputs.Count; i++)
                {
                    TxOut txOut = tx.Outputs[i];

                    if (txOut.IsEmpty)
                        continue;

                    byte[] scriptPubKeyBytes = txOut.ScriptPubKey.ToBytes(true);

                    if (scriptPubKeyBytes.Length == 0 || scriptPubKeyBytes[0] == (byte)OpcodeType.OP_RETURN)
                        continue;

                    var destinations = this.GetDestinations(txOut.ScriptPubKey);

                    bool isChange = destinations.Any(d => addressesOfInterest.Contains(d.ScriptPubKey, out AddressIdentifier address2) && address2.AddressType == 1);

                    if (addSpendTx)
                    {
                        // TODO: Why is this done? If the receipt is not to one of our addresses (i.e. identified in the loop coming next) then why bother trying to record it?
                        this.RecordReceipt(block, null, txOut, tx.IsCoinBase | tx.IsCoinStake, blockTime ?? currentTime, txId, i, isChange);
                    }

                    foreach (TxDestination destination in destinations)
                    {
                        var pubKeyScript = destination.ScriptPubKey;
                        bool containsAddress = addressesOfInterest.Contains(pubKeyScript, out AddressIdentifier address);

                        // Paying to one of our addresses?
                        if (containsAddress && address != null)
                        {
                            // This feature, by design, is agnostic of the type of template being processed.
                            // This type of check is good to have for cold staking though but is catered for in broader terms.
                            // I.e. don't allow any funny business with keys being used with accounts they were not intended for.
                            if (destination is IAccountRestrictedKeyId accountRestrictedKey)
                            {
                                if (accountRestrictedKey.AccountId != address.AccountIndex)
                                    continue;
                            }
                            else
                            {
                                // This tests the converse. 
                                // Don't allow special-purpose accounts (e.g. coldstaking) to be used like normal accounts.
                                // However, for the purposes of recording transactions to watch-only accounts we need to make an allowance.
                                if (address.AccountIndex >= Wallet.SpecialPurposeAccountIndexesStart && address.AccountIndex != Wallet.WatchOnlyAccountIndex)
                                    continue;
                            }

                            // Check if top-up is required.
                            // Get the top-up tracker that applies to this account and address type.
                            ITopUpTracker tracker = this.GetTopUpTracker(address);
                            if (!tracker.IsWatchOnlyAccount)
                            {
                                // If an address inside the address buffer is being used then top-up the buffer.
                                while (address.AddressIndex >= tracker.NextAddressIndex)
                                {
                                    AddressIdentifier newAddress = tracker.CreateAddress();

                                    // Add the new address to our addresses of interest.
                                    addressesOfInterest.AddTentative(Script.FromHex(newAddress.ScriptPubKey), newAddress);
                                }
                            }

                            // Record outputs received by our wallets.
                            this.RecordReceipt(block, pubKeyScript, txOut, tx.IsCoinBase | tx.IsCoinStake, blockTime ?? currentTime, txId, i, address.AddressType == 1);

                            additions = true;

                            transactionsOfInterest.AddTentative(new OutPoint(txId, i), address);
                        }
                    }
                }
            }

            return additions;
        }

        // Used by tests.
        internal void ProcessTransactionData(HdAddress address, ICollection<TransactionData> transactions)
        {
            if (transactions.Count == 0)
                return;

            // Convert relevant information in the block to information that can be joined to the wallet tables.
            IWalletTransactionLookup transactionsOfInterest = this.transactionsOfInterest;
            IWalletAddressLookup addressesOfInterest = this.addressesOfInterest;

            foreach (TransactionData transactionData in transactions)
            {
                var spendingDetails = transactionData.SpendingDetails;
                if (spendingDetails != null)
                {
                    // TODO: Add block hash to spending details.
                    var block = (spendingDetails.BlockHeight == null) ? null :
                        new HashHeightPair(spendingDetails.BlockHash ?? 0, (int)spendingDetails.BlockHeight);

                    Money totalOut = spendingDetails.Change.Concat(spendingDetails.Payments).Sum(d => d.Amount);

                    this.RecordSpend(block, new TxIn() { PrevOut = new OutPoint(transactionData.Id, transactionData.Index) }, address.ScriptPubKey.ToHex(),
                        transactionData.SpendingDetails.IsCoinStake ?? false, transactionData.SpendingDetails.CreationTime.ToUnixTimeSeconds(),
                        totalOut, spendingDetails.TransactionId ?? 0, 0);
                }

                // One per scriptPubKey from scriptPubKey. Original to RedeemScript via TxOut.
                {
                    var block = (transactionData.BlockHeight == null) ? null :
                        new HashHeightPair(transactionData.BlockHash ?? 0, (int)transactionData.BlockHeight);

                    Script scriptPubKey = transactionData.ScriptPubKey;
                    foreach (Script pubKeyScript in this.GetDestinations(scriptPubKey).Select(d => d.ScriptPubKey))
                    {
                        bool containsAddress = addressesOfInterest.Contains(pubKeyScript, out AddressIdentifier targetAddress);

                        this.RecordReceipt(block, pubKeyScript, new TxOut(transactionData.Amount ?? 0, scriptPubKey),
                            (transactionData.IsCoinBase ?? false) || (transactionData.IsCoinStake ?? false),
                            transactionData.CreationTime.ToUnixTimeSeconds(), transactionData.Id, transactionData.Index, address.AddressType == 1);

                        if (containsAddress)
                            transactionsOfInterest.AddTentative(new OutPoint(transactionData.Id, transactionData.Index), targetAddress);
                    }
                }
            }
        }
    }
}
