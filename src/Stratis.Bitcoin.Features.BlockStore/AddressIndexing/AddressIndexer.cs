using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.Bitcoin.Builder.Feature;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Interfaces;

namespace Stratis.Bitcoin.Features.BlockStore.AddressIndexing
{
    /// <summary>Component that builds an index of all addresses and deposits\withdrawals that happened to\from them.</summary>
    public interface IAddressIndexer : IDisposable
    {
        ChainedHeader IndexerTip { get; }

        void Initialize();

        /// <summary>Returns balance of the given address confirmed with at least <paramref name="minConfirmations"/> confirmations.</summary>
        /// <param name="addresses">The set of addresses that will be queried.</param>
        /// <param name="minConfirmations">Only blocks below consensus tip less this parameter will be considered.</param>
        /// <returns>Balance of a given address or <c>null</c> if address wasn't indexed or doesn't exists.</returns>
        AddressBalancesResult GetAddressBalances(string[] addresses, int minConfirmations = 0);

        /// <summary>Returns verbose balances data.</summary>
        /// <param name="addresses">The set of addresses that will be queried.</param>
        /// <returns>See <see cref="VerboseAddressBalancesResult"/>.</returns>
        VerboseAddressBalancesResult GetAddressIndexerState(string[] addresses);

        IFullNodeFeature InitializingFeature { set; }

        LastBalanceDecreaseTransactionModel GetLastBalanceDecreaseTransaction(string address);
    }

    public class AddressIndexer : IAddressIndexer
    {
        private readonly Network network;
        private readonly ICoinView coinView;
        private readonly ChainIndexer chainIndexer;
        private readonly IUtxoIndexer utxoIndexer;
        private readonly IConsensusManager consensusManager;
        private readonly IScriptAddressReader scriptAddressReader;
        private readonly object lockObject;

        public ChainedHeader IndexerTip => GetTip();

        public IFullNodeFeature InitializingFeature { set; private get; }

        /// <summary>Max supported reorganization length for networks without max reorg property.</summary>
        public const int FallBackMaxReorg = 200;

        /// <summary>
        /// This is a window of some blocks that is needed to reduce the consequences of nodes having different view of consensus chain.
        /// We assume that nodes usually don't have view that is different from other nodes by that constant of blocks.
        /// </summary>
        public const int SyncBuffer = 50;

        public AddressIndexer(Network network, ChainIndexer chainIndexer, IScriptAddressReader scriptAddressReader, ICoinView coinView, IUtxoIndexer utxoIndexer, IConsensusManager consensusManager)
        {
            this.network = network;
            this.coinView = coinView;
            this.chainIndexer = chainIndexer;
            this.scriptAddressReader = scriptAddressReader;
            this.consensusManager = consensusManager;
            this.utxoIndexer = utxoIndexer;
            this.lockObject = new object();
        }

        /// <summary>Gets the maxReorg of <see cref="FallBackMaxReorg"/> in case maxReorg is <c>0</c>.</summary>
        /// <param name="network">The network to get the value for.</param>
        /// <returns>Returns the maxReorg or <see cref="FallBackMaxReorg"/> value.</returns>
        public static int GetMaxReorgOrFallbackMaxReorg(Network network)
        {
            int maxReorgLength = network.Consensus.MaxReorgLength == 0 ? FallBackMaxReorg : (int)network.Consensus.MaxReorgLength;

            return maxReorgLength;
        }

        private ChainedHeader GetTip()
        {
            this.coinView.Sync();

            return this.chainIndexer[this.coinView.GetTipHash().Hash];
        }

        public void Initialize()
        {
        }

        private TxDestination AddressToDestination(string address)
        {
            var bitcoinAddress = BitcoinAddress.Create(address, this.network);
            return this.scriptAddressReader.GetDestinationFromScriptPubKey(this.network, bitcoinAddress.ScriptPubKey).Single();
        }

        public AddressBalancesResult GetAddressBalances(string[] addresses, int minConfirmations = 0)
        {
            lock (this.lockObject)
            {
                int cutOff = this.consensusManager.Tip.Height - minConfirmations;

                return new AddressBalancesResult()
                {
                    Balances = addresses
                        .Select(address => (address, destination: AddressToDestination(address)))
                        .Select(t => new AddressBalanceResult()
                        {
                            Address = t.address,
                            Balance = (t.destination == null) ? 0 : new Money(this.coinView.GetBalance(t.destination).First(b => b.height <= cutOff).satoshis),

                        }).ToList()
                };
            }
        }

        public LastBalanceDecreaseTransactionModel GetLastBalanceDecreaseTransaction(string address)
        {
            lock (this.lockObject)
            {
                TxDestination txDestination = AddressToDestination(address);

                AddressBalanceChange lastDecrease = GetChanges(txDestination).FirstOrDefault(b => !b.Deposited);
                if (lastDecrease == null)
                    return null;

                int lastBalanceHeight = lastDecrease.BalanceChangedHeight;

                ChainedHeader header = this.chainIndexer.GetHeader(lastBalanceHeight);

                if (header == null)
                    return null;

                Block block = this.consensusManager.GetBlockData(header.HashBlock).Block;

                if (block == null)
                    return null;

                // Get the UTXO snapshot as of one block lower than the last balance change, so that we are definitely able to look up the inputs of each transaction in the next block.
                ReconstructedCoinviewContext utxos = this.utxoIndexer.GetCoinviewAtHeight(lastBalanceHeight - 1);

                Transaction foundTransaction = null;

                foreach (Transaction transaction in block.Transactions)
                {
                    if (transaction.IsCoinBase)
                        continue;

                    foreach (TxIn txIn in transaction.Inputs)
                    {
                        Transaction prevTx = utxos.Transactions[txIn.PrevOut.Hash];

                        foreach (TxOut txOut in prevTx.Outputs)
                        {
                            if (this.scriptAddressReader.GetAddressFromScriptPubKey(this.network, txOut.ScriptPubKey) == address)
                            {
                                foundTransaction = transaction;
                            }
                        }
                    }
                }

                return foundTransaction == null ? null : new LastBalanceDecreaseTransactionModel() { BlockHeight = lastBalanceHeight, Transaction = new TransactionVerboseModel(foundTransaction, this.network) };
            }
        }

        private IEnumerable<AddressBalanceChange> ToDiff(IEnumerable<AddressBalanceChange> addressBalanceChanges)
        {
            if (addressBalanceChanges.Any())
            {
                AddressBalanceChange nextBalance = addressBalanceChanges.First();

                // Loop in decreasing height order.
                foreach (AddressBalanceChange balance in addressBalanceChanges.Skip(1))
                {
                    yield return new AddressBalanceChange()
                    {
                        BalanceChangedHeight = nextBalance.BalanceChangedHeight,
                        Deposited = nextBalance.Satoshi >= balance.Satoshi,
                        Satoshi = Math.Abs(nextBalance.Satoshi - balance.Satoshi)
                    };

                    nextBalance = balance;
                }

                yield return new AddressBalanceChange()
                {
                    BalanceChangedHeight = nextBalance.BalanceChangedHeight,
                    Deposited = true,
                    Satoshi = nextBalance.Satoshi
                };
            }
        }

        private IEnumerable<AddressBalanceChange> GetChanges(TxDestination txDestination)
        {
            return ToDiff(this.coinView.GetBalance(txDestination).Select(b => new AddressBalanceChange()
            {
                BalanceChangedHeight = (int)b.height,
                Deposited = true,
                Satoshi = b.satoshis
            }));
        }

        /// <inheritdoc/>
        public VerboseAddressBalancesResult GetAddressIndexerState(string[] addresses)
        {
            lock (this.lockObject)
            {
                // If the containing feature is not initialized then wait a bit.
                this.InitializingFeature?.WaitInitialized();

                return new VerboseAddressBalancesResult(this.IndexerTip.Height)
                {
                    BalancesData = addresses
                    .Select(address => (address, destination: AddressToDestination(address)))
                    .Select(t => new AddressIndexerData()
                    {
                        Address = t.address,
                        BalanceChanges = (t.destination == null) ? new List<AddressBalanceChange>() : GetChanges(t.destination).Reverse().ToList() // ToDiff result
                    }).ToList()
                };
            }
        }

        public void Dispose()
        {
        }
    }
}