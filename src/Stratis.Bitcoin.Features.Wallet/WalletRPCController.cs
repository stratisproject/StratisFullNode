using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.RPC.Exceptions;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Utilities;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.Wallet
{
    [ApiVersion("1")]
    public class WalletRPCController : FeatureController
    {
        private readonly IBlockStore blockStore;

        private readonly IBroadcasterManager broadcasterManager;

        private readonly ILogger logger;

        private readonly IScriptAddressReader scriptAddressReader;

        private readonly StoreSettings storeSettings;

        private readonly IWalletManager walletManager;

        private readonly IWalletService walletService;

        private readonly IWalletTransactionHandler walletTransactionHandler;

        private readonly IWalletSyncManager walletSyncManager;

        private readonly IReserveUtxoService reserveUtxoService;

        private readonly WalletSettings walletSettings;

        /// <summary>
        /// The wallet name set by the selectwallet method. This is static since the controller is a stateless type. This value should probably be cached by an injected service in the future.
        /// </summary>
        private static string CurrentWalletName;

        public WalletRPCController(
            IBlockStore blockStore,
            IBroadcasterManager broadcasterManager,
            ChainIndexer chainIndexer,
            IConsensusManager consensusManager,
            IFullNode fullNode,
            ILoggerFactory loggerFactory,
            Network network,
            IScriptAddressReader scriptAddressReader,
            StoreSettings storeSettings,
            IWalletManager walletManager,
            IWalletService walletService,
            WalletSettings walletSettings,
            IWalletTransactionHandler walletTransactionHandler,
            IWalletSyncManager walletSyncManager,
            IReserveUtxoService reserveUtxoService = null) : base(fullNode: fullNode, consensusManager: consensusManager, chainIndexer: chainIndexer, network: network)
        {
            this.blockStore = blockStore;
            this.broadcasterManager = broadcasterManager;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.scriptAddressReader = scriptAddressReader;
            this.storeSettings = storeSettings;
            this.walletManager = walletManager;
            this.walletService = walletService;
            this.walletSettings = walletSettings;
            this.walletTransactionHandler = walletTransactionHandler;
            this.walletSyncManager = walletSyncManager;
            this.reserveUtxoService = reserveUtxoService;
        }

        [ActionName("setwallet")]
        [ActionDescription("Selects the active wallet for RPC based on the name of the wallet supplied.")]
        public bool SetWallet(string walletname)
        {
            WalletRPCController.CurrentWalletName = walletname;
            return true;
        }

        [ActionName("walletpassphrase")]
        [ActionDescription("Stores the wallet decryption key in memory for the indicated number of seconds. Issuing the walletpassphrase command while the wallet is already unlocked will set a new unlock time that overrides the old one.")]
        [NoTrace]
        public bool UnlockWallet(string passphrase, int timeout)
        {
            Guard.NotEmpty(passphrase, nameof(passphrase));

            WalletAccountReference account = this.GetWalletAccountReference();

            try
            {
                this.walletManager.UnlockWallet(passphrase, account.WalletName, timeout);
            }
            catch (SecurityException exception)
            {
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_REQUEST, exception.Message);
            }
            return true; // NOTE: Have to return a value or else RPC middleware doesn't serialize properly.
        }

        [ActionName("walletlock")]
        [ActionDescription("Removes the wallet encryption key from memory, locking the wallet. After calling this method, you will need to call walletpassphrase again before being able to call any methods which require the wallet to be unlocked.")]
        public bool LockWallet()
        {
            WalletAccountReference account = this.GetWalletAccountReference();
            this.walletManager.LockWallet(account.WalletName);
            return true; // NOTE: Have to return a value or else RPC middleware doesn't serialize properly.
        }

        [ActionName("sendtoaddress")]
        [ActionDescription("Sends money to an address. Requires wallet to be unlocked using walletpassphrase.")]
        public async Task<uint256> SendToAddressAsync(BitcoinAddress address, decimal amount, string commentTx, string commentDest)
        {
            TransactionBuildContext context = new TransactionBuildContext(this.FullNode.Network)
            {
                AccountReference = this.GetWalletAccountReference(),
                Recipients = new[] { new Recipient { Amount = Money.Coins(amount), ScriptPubKey = address.ScriptPubKey } }.ToList()
            };

            try
            {
                Transaction transaction = this.walletTransactionHandler.BuildTransaction(context);
                await this.broadcasterManager.BroadcastTransactionAsync(transaction);

                uint256 hash = transaction.GetHash();
                return hash;
            }
            catch (SecurityException)
            {
                throw new RPCServerException(RPCErrorCode.RPC_WALLET_UNLOCK_NEEDED, "Wallet unlock needed");
            }
            catch (WalletException exception)
            {
                throw new RPCServerException(RPCErrorCode.RPC_WALLET_ERROR, exception.Message);
            }
        }

        [ActionName("fundrawtransaction")]
        [ActionDescription("Add inputs to a transaction until it has enough in value to meet its out value. Note that signing is performed separately.")]
        public Task<FundRawTransactionResponse> FundRawTransactionAsync(string rawHex, FundRawTransactionOptions options = null, bool? isWitness = null)
        {
            try
            {
                // TODO: Bitcoin Core performs an heuristic check to determine whether or not the provided transaction should be deserialised with witness data -> core_read.cpp DecodeHexTx()
                Transaction rawTx = this.Network.CreateTransaction();

                // This is an uncommon case where we cannot simply rely on the consensus factory to do the right thing.
                // We need to override the protocol version so that the RPC client workaround functions correctly.
                // If this was not done the transaction deserialisation would attempt to use witness deserialisation and the transaction data would get mangled.
                rawTx.FromBytes(Encoders.Hex.DecodeData(rawHex), this.Network.Consensus.ConsensusFactory, ProtocolVersion.WITNESS_VERSION - 1);

                WalletAccountReference account = this.GetWalletAccountReference();

                HdAddress changeAddress = null;

                // TODO: Support ChangeType properly; allow both 'legacy' and 'bech32'. p2sh-segwit could be added when wallet support progresses to store p2sh redeem scripts
                if (options != null && !string.IsNullOrWhiteSpace(options.ChangeType) && options.ChangeType != "legacy")
                    throw new RPCServerException(RPCErrorCode.RPC_INVALID_PARAMETER, "The change_type option is not yet supported");

                if (options?.ChangeAddress != null)
                {
                    changeAddress = this.walletManager.GetAllAccounts().SelectMany(a => a.GetCombinedAddresses()).FirstOrDefault(a => a.Address == options?.ChangeAddress);
                }
                else
                {
                    changeAddress = this.walletManager.GetUnusedChangeAddress(account);
                }

                if (options?.ChangePosition != null && options.ChangePosition > rawTx.Outputs.Count)
                {
                    throw new RPCServerException(RPCErrorCode.RPC_WALLET_ERROR, "Invalid change position specified!");
                }

                var context = new TransactionBuildContext(this.Network)
                {
                    AccountReference = account,
                    ChangeAddress = changeAddress,
                    OverrideFeeRate = options?.FeeRate,
                    TransactionFee = (options?.FeeRate == null) ? new Money(this.Network.MinRelayTxFee) : null,
                    MinConfirmations = 0,
                    Shuffle = false,
                    UseSegwitChangeAddress = changeAddress != null && (options?.ChangeAddress == changeAddress.Bech32Address),

                    Sign = false
                };

                context.Recipients.AddRange(rawTx.Outputs
                    .Select(s => new Recipient
                    {
                        ScriptPubKey = s.ScriptPubKey,
                        Amount = s.Value,
                        SubtractFeeFromAmount = false // TODO: Do we properly support only subtracting the fee from particular recipients?
                    }));

                context.AllowOtherInputs = true;

                foreach (TxIn transactionInput in rawTx.Inputs)
                    context.SelectedInputs.Add(transactionInput.PrevOut);

                Transaction newTransaction = this.walletTransactionHandler.BuildTransaction(context);

                // If the change position can't be found for some reason, then -1 is the intended default.
                int foundChange = -1;
                if (context.ChangeAddress != null)
                {
                    // Try to find the position of the change and copy it over to the original transaction.
                    // The only logical reason why the change would not be found (apart from errors) is that the chosen input UTXOs were precisely the right size.

                    // Conceivably there could be another output that shares the change address too.
                    // TODO: Could add change position field to the transaction build context to make this check unnecessary
                    if (newTransaction.Outputs.Select(o => o.ScriptPubKey == context.ChangeAddress.ScriptPubKey).Count() > 1)
                    {
                        // This should only happen if the change address was deliberately included in the recipients. So find the output that has a different amount.
                        int index = 0;
                        foreach (TxOut newTransactionOutput in newTransaction.Outputs)
                        {
                            if (newTransactionOutput.ScriptPubKey == context.ChangeAddress.ScriptPubKey)
                            {
                                // Set this regardless. It will be overwritten if a subsequent output is the 'correct' change output.
                                // If all potential change outputs have identical values it won't be updated, but in that case any of them are acceptable as the 'real' change output.
                                if (foundChange == -1)
                                    foundChange = index;

                                // TODO: When SubtractFeeFromAmount is set this amount check will no longer be valid as they won't be equal
                                // If the amount was not in the recipients list then it must be the change output.
                                if (!context.Recipients.Any(recipient => recipient.ScriptPubKey == newTransactionOutput.ScriptPubKey && recipient.Amount == newTransactionOutput.Value))
                                    foundChange = index;
                            }

                            index++;
                        }
                    }
                    else
                    {
                        int index = 0;
                        foreach (TxOut newTransactionOutput in newTransaction.Outputs)
                        {
                            if (newTransactionOutput.ScriptPubKey == context.ChangeAddress.ScriptPubKey)
                            {
                                foundChange = index;
                            }

                            index++;
                        }
                    }

                    if (foundChange != -1)
                    {
                        // The position the change will be copied from in the transaction.
                        int tempPos = foundChange;

                        // Just overwrite this to avoid introducing yet another change position variable to the outer scope.
                        // We need to update the foundChange value to return it in the RPC response as the final change position.
                        foundChange = options?.ChangePosition ?? (RandomUtils.GetInt32() % rawTx.Outputs.Count);

                        rawTx.Outputs.Insert(foundChange, newTransaction.Outputs[tempPos]);
                    }
                    else
                    {
                        // This should never happen so it is better to error out than potentially return incorrect results.
                        throw new RPCServerException(RPCErrorCode.RPC_WALLET_ERROR, "Unable to locate change output in built transaction!");
                    }
                }

                // TODO: Copy any updated output amounts, which might have changed due to the subtractfee flags etc (this also includes spreading the fee over the selected outputs, if applicable)

                // Copy all the inputs from the built transaction into the original.
                // As they are unsigned this has no effect on transaction validity.
                foreach (TxIn newTransactionInput in newTransaction.Inputs)
                {
                    if (!context.SelectedInputs.Contains(newTransactionInput.PrevOut))
                    {
                        rawTx.Inputs.Add(newTransactionInput);

                        if (options?.LockUnspents ?? false)
                        {
                            if (this.reserveUtxoService == null)
                                continue;

                            // Prevent the provided UTXO from being spent by another transaction until this one is signed and broadcast.
                            this.reserveUtxoService.ReserveUtxos(new[] { newTransactionInput.PrevOut });
                        }
                    }
                }

                return Task.FromResult(new FundRawTransactionResponse()
                {
                    ChangePos = foundChange,
                    Fee = context.TransactionFee,
                    Transaction = rawTx
                });
            }
            catch (SecurityException)
            {
                throw new RPCServerException(RPCErrorCode.RPC_WALLET_UNLOCK_NEEDED, "Wallet unlock needed");
            }
            catch (WalletException exception)
            {
                throw new RPCServerException(RPCErrorCode.RPC_WALLET_ERROR, exception.Message);
            }
        }

        /// <summary>
        /// Sign inputs for raw transaction.
        /// Needed private keys need to be within the wallet's current gap limit.
        /// </summary>
        /// <param name="rawHex">The raw (unsigned) transaction in hex format.</param>
        /// <returns>The hex format of the transaction once it has been signed.</returns>
        [ActionName("signrawtransaction")]
        [ActionDescription("Sign inputs for raw transaction. Requires all affected wallets to be unlocked using walletpassphrase.")]
        public Task<SignRawTransactionResponse> SignRawTransactionAsync(string rawHex)
        {
            try
            {
                Transaction rawTx = this.Network.CreateTransaction(rawHex);

                // We essentially need to locate the needed private keys from within the wallet and sign with them.
                // This is done by listing the UTXOs for each address and seeing if one of them matches each input in the unsigned transaction.
                var builder = new TransactionBuilder(this.Network);
                var coins = new List<Coin>();
                var signingKeys = new List<ISecret>();

                // TODO: Add a cache to speed up the case where multiple inputs are controlled by the same private key?
                foreach (var input in rawTx.Inputs.ToArray())
                {
                    bool found = false;

                    // We need to know which wallet it was that we found the correct UTXO inside, as an account maintains no reference to its parent wallet.
                    foreach (Wallet wallet in this.walletManager.GetWallets())
                    {
                        if (found)
                            break;

                        foreach (var unspent in wallet.GetAllUnspentTransactions(this.ChainIndexer.Height).Where(a => (a.Transaction.Id == input.PrevOut.Hash && a.Transaction.Index == input.PrevOut.N)))
                        {
                            coins.Add(new Coin(unspent.Transaction.Id, (uint)unspent.Transaction.Index, unspent.Transaction.Amount, unspent.Transaction.ScriptPubKey));

                            ExtKey seedExtKey = this.walletManager.GetExtKey(new WalletAccountReference() { AccountName = unspent.Account.Name, WalletName = wallet.Name });
                            ExtKey addressExtKey = seedExtKey.Derive(new KeyPath(unspent.Address.HdPath));
                            BitcoinExtKey addressPrivateKey = addressExtKey.GetWif(wallet.Network);
                            signingKeys.Add(addressPrivateKey);

                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        throw new RPCServerException(RPCErrorCode.RPC_WALLET_ERROR, "Unable to locate private key for transaction input!");
                    }
                }

                builder.AddCoins(coins);
                builder.AddKeys(signingKeys.ToArray());
                builder.SignTransactionInPlace(rawTx);

                return Task.FromResult(new SignRawTransactionResponse()
                {
                    Transaction = rawTx,
                    Complete = true
                });
            }
            catch (SecurityException)
            {
                throw new RPCServerException(RPCErrorCode.RPC_WALLET_UNLOCK_NEEDED, "Wallet unlock needed");
            }
            catch (WalletException exception)
            {
                throw new RPCServerException(RPCErrorCode.RPC_WALLET_ERROR, exception.Message);
            }
        }

        /// <summary>
        /// Broadcasts a raw transaction from hex to local node and network.
        /// </summary>
        /// <param name="hex">Raw transaction in hex.</param>
        /// <returns>The transaction hash.</returns>
        [ActionName("sendrawtransaction")]
        [ActionDescription("Submits raw transaction (serialized, hex-encoded) to local node and network.")]
        public async Task<uint256> SendTransactionAsync(string hex)
        {
            Transaction transaction = this.FullNode.Network.CreateTransaction(hex);
            await this.broadcasterManager.BroadcastTransactionAsync(transaction);

            uint256 hash = transaction.GetHash();

            return hash;
        }

        /// <summary>
        /// RPC method that gets a new address for receiving payments.
        /// Uses the first wallet and account.
        /// </summary>
        /// <param name="account">Parameter is deprecated.</param>
        /// <param name="addressType">Address type, currently only 'legacy' is supported.</param>
        /// <returns>The new address.</returns>
        [ActionName("getnewaddress")]
        [ActionDescription("Returns a new wallet address for receiving payments.")]
        public NewAddressModel GetNewAddress(string account, string addressType)
        {
            if (!string.IsNullOrEmpty(account))
                throw new RPCServerException(RPCErrorCode.RPC_METHOD_DEPRECATED, "Use of 'account' parameter has been deprecated");

            if (!string.IsNullOrEmpty(addressType))
            {
                // Currently segwit and bech32 addresses are not supported.
                if (!addressType.Equals("legacy", StringComparison.InvariantCultureIgnoreCase))
                    throw new RPCServerException(RPCErrorCode.RPC_METHOD_NOT_FOUND, "Only address type 'legacy' is currently supported.");
            }

            var walletAccountReference = this.GetWalletAccountReference();
            var hdAddress = this.walletManager.GetNewAddresses(walletAccountReference, 1).FirstOrDefault();

            string base58Address = hdAddress.Address;

            return new NewAddressModel(base58Address);
        }

        /// <summary>
        /// RPC method that returns the total available balance.
        /// The available balance is what the wallet considers currently spendable.
        ///
        /// Uses the first wallet and account.
        /// </summary>
        /// <param name="accountName">Remains for backward compatibility. Must be excluded or set to "*" or "". Deprecated in latest bitcoin core (0.17.0).</param>
        /// <param name="minConfirmations">Only include transactions confirmed at least this many times. (default=0)</param>
        /// <returns>Total spendable balance of the wallet.</returns>
        [ActionName("getbalance")]
        [ActionDescription("Gets wallets spendable balance.")]
        public decimal GetBalance(string accountName, int minConfirmations = 0)
        {
            if (!string.IsNullOrEmpty(accountName) && !accountName.Equals("*"))
                throw new RPCServerException(RPCErrorCode.RPC_METHOD_DEPRECATED, "Account has been deprecated, must be excluded or set to \"*\"");

            WalletAccountReference account = this.GetWalletAccountReference();

            AccountBalance balances = this.walletManager.GetBalances(account.WalletName, account.AccountName, minConfirmations).FirstOrDefault();
            return balances?.SpendableAmount.ToUnit(MoneyUnit.BTC) ?? 0;
        }

        /// <summary>
        /// RPC method to return transaction info from the wallet. Will only work fully if 'txindex' is set.
        /// Uses the default wallet if specified, or the first wallet found.
        /// </summary>
        /// <param name="txid">Identifier of the transaction to find.</param>
        /// <param name="include_watchonly">Set to <c>true</c> to search the watch-only account.</param>
        /// <returns>Transaction information.</returns>
        [ActionName("gettransaction")]
        [ActionDescription("Get detailed information about an in-wallet transaction.")]
        public Task<object> GetTransaction(string txid, bool include_watchonly = false)
        {
            if (!uint256.TryParse(txid, out uint256 trxid))
                throw new ArgumentException(nameof(txid));

            if (include_watchonly)
            {
                WalletHistoryModel history = GetWatchOnlyTransaction(trxid);
                if ((history?.AccountsHistoryModel?.FirstOrDefault()?.TransactionsHistory?.Count ?? 0) != 0)
                    return Task.FromResult<object>(history);
            }

            // First check the regular wallet accounts.
            WalletAccountReference accountReference = this.GetWalletAccountReference();

            Wallet hdWallet = this.walletManager.WalletRepository.GetWallet(accountReference.WalletName);
            HdAccount hdAccount = this.walletManager.WalletRepository.GetAccounts(hdWallet, accountReference.AccountName).First();

            IWalletAddressReadOnlyLookup addressLookup = this.walletManager.WalletRepository.GetWalletAddressLookup(accountReference.WalletName);

            bool IsChangeAddress(Script scriptPubKey)
            {
                return addressLookup.Contains(scriptPubKey, out AddressIdentifier addressIdentifier) && addressIdentifier.AddressType == 1;
            }

            // Get the transaction from the wallet by looking into received and send transactions.
            List<TransactionData> receivedTransactions = this.walletManager.WalletRepository.GetTransactionOutputs(hdAccount, null, trxid, true)
                .Where(td => !IsChangeAddress(td.ScriptPubKey)).ToList();
            List<TransactionData> sentTransactions = this.walletManager.WalletRepository.GetTransactionInputs(hdAccount, null, trxid, true).ToList();

            TransactionData firstReceivedTransaction = receivedTransactions.FirstOrDefault();
            TransactionData firstSendTransaction = sentTransactions.FirstOrDefault();

            if (firstReceivedTransaction == null && firstSendTransaction == null)
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY, "Invalid or non-wallet transaction id.");

            uint256 blockHash = null;
            int? blockHeight, blockIndex;
            DateTimeOffset transactionTime;
            SpendingDetails spendingDetails = firstSendTransaction?.SpendingDetails;

            if (firstReceivedTransaction != null)
            {
                blockHeight = firstReceivedTransaction.BlockHeight;
                blockIndex = firstReceivedTransaction.BlockIndex;
                blockHash = firstReceivedTransaction.BlockHash;
                transactionTime = firstReceivedTransaction.CreationTime;
            }
            else
            {
                blockHeight = spendingDetails.BlockHeight;
                blockIndex = spendingDetails.BlockIndex;
                blockHash = spendingDetails.BlockHash;
                transactionTime = spendingDetails.CreationTime;
            }

            // Get the block containing the transaction (if it has been confirmed).
            ChainedHeaderBlock chainedHeaderBlock = null;
            if (blockHash != null)
                this.ConsensusManager.GetOrDownloadBlocks(new List<uint256> { blockHash }, b => { chainedHeaderBlock = b; });

            Block block = null;
            Transaction transactionFromStore = null;
            if (chainedHeaderBlock != null)
            {
                block = chainedHeaderBlock.Block;
                if (block != null)
                {
                    if (blockIndex == null)
                        blockIndex = block.Transactions.FindIndex(t => t.GetHash() == trxid);

                    transactionFromStore = block.Transactions[(int)blockIndex];
                }
            }

            bool isGenerated;
            string hex;
            if (transactionFromStore != null)
            {
                transactionTime = Utils.UnixTimeToDateTime(chainedHeaderBlock.ChainedHeader.Header.Time);
                isGenerated = transactionFromStore.IsCoinBase || transactionFromStore.IsCoinStake;
                hex = transactionFromStore.ToHex();
            }
            else
            {
                isGenerated = false;
                hex = null; // TODO get from mempool
            }

            var model = new GetTransactionModel
            {
                Confirmations = blockHeight != null ? this.ConsensusManager.Tip.Height - blockHeight.Value + 1 : 0,
                Isgenerated = isGenerated ? true : (bool?)null,
                BlockHash = blockHash,
                BlockIndex = blockIndex,
                BlockTime = block?.Header.BlockTime.ToUnixTimeSeconds(),
                TransactionId = uint256.Parse(txid),
                TransactionTime = transactionTime.ToUnixTimeSeconds(),
                TimeReceived = transactionTime.ToUnixTimeSeconds(),
                Details = new List<GetTransactionDetailsModel>(),
                Hex = hex
            };

            // Send transactions details.
            if (spendingDetails != null)
            {
                Money feeSent = Money.Zero;
                if (firstSendTransaction != null)
                {
                    // Get the change.
                    long change = spendingDetails.Change.Sum(o => o.Amount);

                    Money inputsAmount = new Money(sentTransactions.Sum(i => i.Amount));
                    Money outputsAmount = new Money(spendingDetails.Payments.Sum(p => p.Amount) + change);

                    feeSent = inputsAmount - outputsAmount;
                }

                var details = spendingDetails.Payments
                    .GroupBy(detail => detail.DestinationAddress)
                    .Select(p => new GetTransactionDetailsModel()
                    {
                        Address = p.Key,
                        Category = GetTransactionDetailsCategoryModel.Send,
                        OutputIndex = p.First().OutputIndex,
                        Amount = 0 - p.Sum(detail => detail.Amount.ToDecimal(MoneyUnit.BTC)),
                        Fee = -feeSent.ToDecimal(MoneyUnit.BTC)
                    });

                model.Details.AddRange(details);
            }

            // Get the ColdStaking script template if available.
            Dictionary<string, ScriptTemplate> templates = this.walletManager.GetValidStakingTemplates();
            ScriptTemplate coldStakingTemplate = templates.ContainsKey("ColdStaking") ? templates["ColdStaking"] : null;

            // Receive transactions details.
            IScriptAddressReader scriptAddressReader = this.FullNode.NodeService<IScriptAddressReader>();
            foreach (TransactionData trxInWallet in receivedTransactions)
            {
                // Skip the details if the script pub key is cold staking.
                // TODO: Verify if we actually need this any longer, after changing the internals to recognize account type
                if (coldStakingTemplate != null && coldStakingTemplate.CheckScriptPubKey(trxInWallet.ScriptPubKey))
                {
                    continue;
                }

                GetTransactionDetailsCategoryModel category;

                if (isGenerated)
                {
                    category = model.Confirmations > this.FullNode.Network.Consensus.CoinbaseMaturity ? GetTransactionDetailsCategoryModel.Generate : GetTransactionDetailsCategoryModel.Immature;
                }
                else
                {
                    category = GetTransactionDetailsCategoryModel.Receive;
                }

                string address = scriptAddressReader.GetAddressFromScriptPubKey(this.FullNode.Network, trxInWallet.ScriptPubKey);

                model.Details.Add(new GetTransactionDetailsModel
                {
                    Address = address,
                    Category = category,
                    Amount = trxInWallet.Amount.ToDecimal(MoneyUnit.BTC),
                    OutputIndex = trxInWallet.Index
                    // TODO: Fee is null here - is that correct?
                });
            }

            model.Amount = model.Details.Sum(d => d.Amount);
            model.Fee = model.Details.FirstOrDefault(d => d.Category == GetTransactionDetailsCategoryModel.Send)?.Fee;

            return Task.FromResult<object>(model);
        }


        /// <summary>
        /// We get the details via the wallet service's history method.
        /// </summary>
        /// <param name="trxid">The hash of the transaction to get.</param>
        /// <returns>See <see cref="WalletHistoryModel"/>.</returns>
        private WalletHistoryModel GetWatchOnlyTransaction(uint256 trxid)
        {
            var accountReference = this.GetWatchOnlyWalletAccountReference();

            var request = new WalletHistoryRequest()
            {
                WalletName = accountReference.WalletName,
                AccountName = Wallet.WatchOnlyAccountName,
                SearchQuery = trxid.ToString(),
            };

            var history = this.walletService.GetHistory(request);
            return history;
        }

        [ActionName("importpubkey")]
        public bool ImportPubkey(string pubkey, string label = "", bool rescan = true)
        {
            WalletAccountReference walletAccountReference = this.GetWatchOnlyWalletAccountReference();

            this.walletManager.AddWatchOnlyAddress(walletAccountReference.WalletName, walletAccountReference.AccountName,
                pubkey.Split(new char[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(pk => new PubKey(pk.Trim())).ToArray());

            // As we cannot be sure when an imported pubkey was transacted against, we have to rescan from genesis if requested.
            if (rescan)
            {
                this.walletSyncManager.SyncFromHeight(0, walletAccountReference.WalletName);
            }

            return true;
        }

        [ActionName("listaddressgroupings")]
        [ActionDescription("Returns a list of grouped addresses which have had their common ownership made public by common use as inputs or as the resulting change in past transactions.")]
        public List<object> ListAddressGroupings()
        {
            var walletReference = this.GetWalletAccountReference();
            var addressGroupings = this.walletManager.GetAddressGroupings(walletReference.WalletName);

            var groupingObject = new List<object> { };

            foreach (var addressGrouping in addressGroupings)
            {
                var inner = new List<object> { };

                foreach (var address in addressGrouping)
                {
                    var balance = this.walletManager.GetAddressBalance(address);
                    inner.Add(new { address, balance.AmountConfirmed.Satoshi });
                }

                var innerValues = JArray.FromObject(inner).Select(x => x.Values());

                groupingObject.Add(innerValues);
            }

            return groupingObject;
        }

        /// <summary>
        /// This will check the wallet's list of <see cref="HdAccount.InternalAddress"/>es to see if this address is
        /// an address that received change.
        /// </summary>
        /// <param name="internalAddresses">The wallet's set of internal addresses.</param>
        /// <param name="txOutScriptPubkey">The base58 address to verify from the <see cref="TxOut"/>.</param>
        /// <returns><c>true</c> if the <paramref name="txOutScriptPubkey"/> is a change address.</returns>
        private bool IsChange(IEnumerable<HdAddress> internalAddresses, Script txOutScriptPubkey)
        {
            return internalAddresses.FirstOrDefault(ia => ia.ScriptPubKey == txOutScriptPubkey) != null;
        }

        /// <summary>
        /// Determines whether or not the input's address exists in the wallet's set of addresses.
        /// </summary>
        /// <param name="addresses">The wallet's external and internal addresses.</param>
        /// <param name="txDictionary">The set of transactions to check against.</param>
        /// <param name="txIn">The input to check.</param>
        /// <returns><c>true</c>if the input's address exist in the wallet.</returns>
        private bool IsTxInMine(IEnumerable<HdAddress> addresses, Dictionary<uint256, TransactionData> txDictionary, TxIn txIn)
        {
            TransactionData previousTransaction = null;
            txDictionary.TryGetValue(txIn.PrevOut.Hash, out previousTransaction);

            if (previousTransaction == null)
                return false;

            var previousTx = this.blockStore.GetTransactionById(previousTransaction.Id);
            if (txIn.PrevOut.N >= previousTx.Outputs.Count)
                return false;

            // We now need to check if the scriptPubkey is in our wallet.
            // See https://github.com/bitcoin/bitcoin/blob/011c39c2969420d7ca8b40fbf6f3364fe72da2d0/src/script/ismine.cpp
            return IsAddressMine(addresses, previousTx.Outputs[txIn.PrevOut.N].ScriptPubKey);
        }

        /// <summary>
        /// Determines whether the script translates to an address that exists in the given wallet.
        /// </summary>
        /// <param name="addresses">All the addresses from the wallet.</param>
        /// <param name="scriptPubKey">The script to check.</param>
        /// <returns><c>true</c> if the <paramref name="scriptPubKey"/> is an address in the given wallet.</returns>
        private bool IsAddressMine(IEnumerable<HdAddress> addresses, Script scriptPubKey)
        {
            return addresses.FirstOrDefault(a => a.ScriptPubKey == scriptPubKey) != null;
        }

        [ActionName("listunspent")]
        [ActionDescription("Returns an array of unspent transaction outputs belonging to this wallet.")]
        public UnspentCoinModel[] ListUnspent(int minConfirmations = 1, int maxConfirmations = 9999999, string addressesJson = null)
        {
            List<BitcoinAddress> addresses = new List<BitcoinAddress>();
            if (!string.IsNullOrEmpty(addressesJson))
            {
                JsonConvert.DeserializeObject<List<string>>(addressesJson).ForEach(i => addresses.Add(BitcoinAddress.Create(i, this.FullNode.Network)));
            }

            string walletName = this.GetWallet();
            var accounts = this.walletManager.GetAccounts(walletName, Wallet.AllAccounts);
            var unspentCoins = new List<UnspentCoinModel>();

            foreach (var account in accounts)
            {
                // The intention here is to filter out cold staking accounts. The watch only account can be included.
                if (!account.IsNormalAccount() && account.Name != Wallet.WatchOnlyAccountName)
                    continue;

                WalletAccountReference accountReference = new WalletAccountReference(walletName, account.Name);
            
                IEnumerable<UnspentOutputReference> spendableTransactions = this.walletManager.GetSpendableTransactionsInAccount(accountReference, minConfirmations);

                foreach (var spendableTx in spendableTransactions)
                {
                    if (spendableTx.Confirmations > maxConfirmations)
                        continue;

                    if (addresses.Any() && !addresses.Contains(BitcoinAddress.Create(spendableTx.Address.Address, this.FullNode.Network)))
                        continue;

                    unspentCoins.Add(new UnspentCoinModel()
                    {
                        Account = accountReference.AccountName,
                        Address = spendableTx.Address.Address,
                        Id = spendableTx.Transaction.Id,
                        Index = spendableTx.Transaction.Index,
                        Amount = spendableTx.Transaction.Amount,
                        ScriptPubKeyHex = spendableTx.Transaction.ScriptPubKey.ToHex(),
                        RedeemScriptHex = null, // TODO: Currently don't support P2SH wallet addresses, review if we do.
                        Confirmations = spendableTx.Confirmations,
                        IsSpendable = (!spendableTx.Transaction.IsSpent()) && (spendableTx.Account.Name != Wallet.WatchOnlyAccountName),
                        IsSolvable = (!spendableTx.Transaction.IsSpent()) && (spendableTx.Account.Name != Wallet.WatchOnlyAccountName) // If it's spendable we assume it's solvable.
                    });
                }
            }

            return unspentCoins.ToArray();
        }

        [ActionName("sendmany")]
        [ActionDescription("Creates and broadcasts a transaction which sends outputs to multiple addresses.")]
        public async Task<uint256> SendManyAsync(string fromAccount, string addressesJson, int minConf = 1, string comment = null, string subtractFeeFromJson = null, bool isReplaceable = false, int? confTarget = null, string estimateMode = "UNSET")
        {
            if (string.IsNullOrEmpty(addressesJson))
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_PARAMETER, "No valid output addresses specified.");

            var addresses = new Dictionary<string, decimal>();
            try
            {
                // Outputs addresses are key-value pairs of address, amount. Translate to Receipient list.
                addresses = JsonConvert.DeserializeObject<Dictionary<string, decimal>>(addressesJson);
            }
            catch (JsonSerializationException ex)
            {
                throw new RPCServerException(RPCErrorCode.RPC_PARSE_ERROR, ex.Message);
            }

            if (addresses.Count == 0)
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_PARAMETER, "No valid output addresses specified.");

            // Optional list of addresses to subtract fees from.
            IEnumerable<BitcoinAddress> subtractFeeFromAddresses = null;
            if (!string.IsNullOrEmpty(subtractFeeFromJson))
            {
                try
                {
                    subtractFeeFromAddresses = JsonConvert.DeserializeObject<List<string>>(subtractFeeFromJson).Select(i => BitcoinAddress.Create(i, this.FullNode.Network));
                }
                catch (JsonSerializationException ex)
                {
                    throw new RPCServerException(RPCErrorCode.RPC_PARSE_ERROR, ex.Message);
                }
            }

            var recipients = new List<Recipient>();
            foreach (var address in addresses)
            {
                // Check for duplicate recipients
                var recipientAddress = BitcoinAddress.Create(address.Key, this.FullNode.Network).ScriptPubKey;
                if (recipients.Any(r => r.ScriptPubKey == recipientAddress))
                    throw new RPCServerException(RPCErrorCode.RPC_INVALID_PARAMETER, string.Format("Invalid parameter, duplicated address: {0}.", recipientAddress));

                var recipient = new Recipient
                {
                    ScriptPubKey = recipientAddress,
                    Amount = Money.Coins(address.Value),
                    SubtractFeeFromAmount = subtractFeeFromAddresses == null ? false : subtractFeeFromAddresses.Contains(BitcoinAddress.Create(address.Key, this.FullNode.Network))
                };

                recipients.Add(recipient);
            }

            WalletAccountReference accountReference = this.GetWalletAccountReference();

            var context = new TransactionBuildContext(this.FullNode.Network)
            {
                AccountReference = accountReference,
                MinConfirmations = minConf,
                Shuffle = true, // We shuffle transaction outputs by default as it's better for anonymity.
                Recipients = recipients
            };

            // Set fee type for transaction build context.
            context.FeeType = FeeType.Medium;

            if (estimateMode.Equals("ECONOMICAL", StringComparison.InvariantCultureIgnoreCase))
                context.FeeType = FeeType.Low;

            else if (estimateMode.Equals("CONSERVATIVE", StringComparison.InvariantCultureIgnoreCase))
                context.FeeType = FeeType.High;

            try
            {
                // Log warnings for currently unsupported parameters.
                if (!string.IsNullOrEmpty(comment))
                    this.logger.LogWarning("'comment' parameter is currently unsupported. Ignored.");

                if (isReplaceable)
                    this.logger.LogWarning("'replaceable' parameter is currently unsupported. Ignored.");

                if (confTarget != null)
                    this.logger.LogWarning("'conf_target' parameter is currently unsupported. Ignored.");

                Transaction transaction = this.walletTransactionHandler.BuildTransaction(context);
                await this.broadcasterManager.BroadcastTransactionAsync(transaction);

                return transaction.GetHash();
            }
            catch (SecurityException)
            {
                throw new RPCServerException(RPCErrorCode.RPC_WALLET_UNLOCK_NEEDED, "Wallet unlock needed");
            }
            catch (WalletException exception)
            {
                throw new RPCServerException(RPCErrorCode.RPC_WALLET_ERROR, exception.Message);
            }
            catch (NotImplementedException exception)
            {
                throw new RPCServerException(RPCErrorCode.RPC_MISC_ERROR, exception.Message);
            }
        }

        [ActionName("getwalletinfo")]
        [ActionDescription("Provides information about the wallet.")]
        public GetWalletInfoModel GetWalletInfo()
        {
            var accountReference = this.GetWalletAccountReference();
            var account = this.walletManager.GetAccounts(accountReference.WalletName)
                .Where(i => i.Name.Equals(accountReference.AccountName))
                .Single();

            (Money confirmedAmount, Money unconfirmedAmount) = account.GetBalances(account.IsNormalAccount());

            var balance = Money.Coins(GetBalance(string.Empty));
            var immature = Money.Coins(balance.ToDecimal(MoneyUnit.BTC) - GetBalance(string.Empty, (int)this.FullNode.Network.Consensus.CoinbaseMaturity)); // Balance - Balance(AtHeight)

            var model = new GetWalletInfoModel
            {
                Balance = balance,
                WalletName = accountReference.WalletName + ".wallet.json",
                WalletVersion = 1,
                UnConfirmedBalance = unconfirmedAmount,
                ImmatureBalance = immature
            };

            return model;
        }

        private int GetConfirmationCount(TransactionData transaction)
        {
            if (transaction.BlockHeight.HasValue)
            {
                var blockCount = this.ConsensusManager?.Tip.Height ?? -1; // TODO: This is available in FullNodeController, should refactor and reuse the logic.
                return blockCount - transaction.BlockHeight.Value;
            }

            return -1;
        }

        /// <summary>
        /// Gets the name of the wallet currently associated with RPC. This can be changed via the 'setwallet' RPC command.
        /// </summary>
        /// <returns>The active wallet name.</returns>
        private string GetWallet()
        {
            string walletName = null;

            if (string.IsNullOrWhiteSpace(WalletRPCController.CurrentWalletName))
            {
                if (this.walletSettings.IsDefaultWalletEnabled())
                    walletName = this.walletManager.GetWalletsNames().FirstOrDefault(w => w == this.walletSettings.DefaultWalletName);
                else
                {
                    // TODO: Support multi wallet like core by mapping passed RPC credentials to a wallet/account
                    walletName = this.walletManager.GetWalletsNames().FirstOrDefault();
                }
            }
            else
            {
                // Read the wallet name from the class instance.
                walletName = WalletRPCController.CurrentWalletName;
            }

            if (walletName == null)
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_REQUEST, "No wallet found");

            return walletName;
        }

        /// <summary>
        /// Gets the first account from the "default" wallet if it is specified,
        /// otherwise returns the first available account in the existing wallets.
        /// </summary>
        /// <returns>Reference to the default wallet account, or the first available if no default wallet is specified.</returns>
        private WalletAccountReference GetWalletAccountReference()
        {
            string walletName = GetWallet();

            HdAccount account = this.walletManager.GetAccounts(walletName).FirstOrDefault();

            if (account == null)
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_REQUEST, "Account not found");

            return new WalletAccountReference(walletName, account.Name);
        }

        /// <summary>
        /// Gets the first watch only account from the "default" wallet if it is specified,
        /// otherwise returns the first available watch only account in the existing wallets.
        /// </summary>
        /// <returns>Reference to the default wallet watch only account, or the first available if no default wallet is specified.</returns>
        private WalletAccountReference GetWatchOnlyWalletAccountReference()
        {
            string walletName = GetWallet();

            if (walletName == null)
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_REQUEST, "No wallet found");

            HdAccount account = this.walletManager.GetOrCreateWatchOnlyAccount(walletName);

            if (account == null)
                throw new RPCServerException(RPCErrorCode.RPC_INVALID_REQUEST, "Unable to retrieve watch only account");

            return new WalletAccountReference(walletName, account.Name);
        }
    }
}
