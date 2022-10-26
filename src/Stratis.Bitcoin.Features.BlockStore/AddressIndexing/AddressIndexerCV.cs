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

    public class AddressIndexerCV : IAddressIndexer
    {
        private readonly Network network;
        private readonly ICoinView coinView;
        private readonly ChainIndexer chainIndexer;
        private readonly IScriptAddressReader scriptAddressReader;

        public ChainedHeader IndexerTip => GetTip();

        public IFullNodeFeature InitializingFeature { set; private get; }

        /// <summary>Max supported reorganization length for networks without max reorg property.</summary>
        public const int FallBackMaxReorg = 200;

        /// <summary>
        /// This is a window of some blocks that is needed to reduce the consequences of nodes having different view of consensus chain.
        /// We assume that nodes usually don't have view that is different from other nodes by that constant of blocks.
        /// </summary>
        public const int SyncBuffer = 50;

        public AddressIndexerCV(Network network, ChainIndexer chainIndexer, IScriptAddressReader scriptAddressReader, ICoinView coinView)
        {
            this.network = network;
            this.coinView = coinView;
            this.chainIndexer = chainIndexer;
            this.scriptAddressReader = scriptAddressReader;
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
            return new AddressBalancesResult()
            {
                Balances = addresses
                    .Select(address => (address, destination: AddressToDestination(address)))
                    .Select(t => new AddressBalanceResult()
                    {
                        Address = t.address,
                        Balance = (t.destination == null) ? 0 : new Money(this.coinView.GetBalance(t.destination).First(x => x.height <= (this.chainIndexer.Tip.Height - minConfirmations)).satoshis),

                    }).ToList()
            };
        }

        public LastBalanceDecreaseTransactionModel GetLastBalanceDecreaseTransaction(string address)
        {
            throw new NotImplementedException();
        }

        private IEnumerable<AddressBalanceChange> ToDiff(List<AddressBalanceChange> addressBalanceChanges)
        {
            for (int i = addressBalanceChanges.Count - 1; i > 0; i--)
            {
                yield return new AddressBalanceChange()
                {
                    BalanceChangedHeight = addressBalanceChanges[i - 1].BalanceChangedHeight,
                    Deposited = addressBalanceChanges[i - 1].Satoshi >= addressBalanceChanges[i].Satoshi,
                    Satoshi = Math.Abs(addressBalanceChanges[i - 1].Satoshi - addressBalanceChanges[i].Satoshi)
                };
            }
        }

        /// <inheritdoc/>
        public VerboseAddressBalancesResult GetAddressIndexerState(string[] addresses)
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
                        BalanceChanges = (t.destination == null) ? new List<AddressBalanceChange>() : ToDiff(this.coinView.GetBalance(t.destination).Select(b => new AddressBalanceChange()
                        {
                            BalanceChangedHeight = (int)b.height,
                            Deposited = b.satoshis >= 0,
                            Satoshi = Math.Abs(b.satoshis)
                        }).ToList()).ToList()
                    }).ToList()
            };
        }

        public void Dispose()
        {
        }
    }
}