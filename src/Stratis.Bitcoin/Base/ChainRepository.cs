using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Base
{
    public interface IChainRepository : IDisposable
    {
        /// <summary>Loads the chain of headers from the database.</summary>
        /// <returns>Tip of the loaded chain.</returns>
        Task<ChainedHeader> LoadAsync(ChainedHeader genesisHeader);

        /// <summary>Persists chain of headers to the database.</summary>
        Task SaveAsync(ChainIndexer chainIndexer);
    }

    public class ChainRepository : IChainRepository
    {
        private readonly IChainStore chainStore;
        private readonly ILogger logger;
        private readonly ISignals signals;

        private BlockLocator locator;

        public ChainRepository(IChainStore chainStore, ISignals signals = null)
        {
            this.chainStore = chainStore;
            this.signals = signals;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <inheritdoc />
        public Task<ChainedHeader> LoadAsync(ChainedHeader genesisHeader)
        {
            Task<ChainedHeader> task = Task.Run(() =>
            {
                ChainedHeader tip = null;

                ChainData data = this.chainStore.GetChainData(0);

                if (data == null)
                {
                    genesisHeader.SetChainStore(this.chainStore);
                    return genesisHeader;
                }

                Guard.Assert(data.Hash == genesisHeader.HashBlock); // can't swap networks

                int height = 0;

                while (true)
                {
                    data = this.chainStore.GetChainData(height);

                    if (data == null)
                        break;

                    tip = new ChainedHeader(data.Hash, data.Work, tip);
                    if (tip.Height == 0)
                        tip.SetChainStore(this.chainStore);

                    if (height % 50_000 == 0)
                    {
                        if (this.signals != null)
                            this.signals.Publish(new FullNodeEvent() { Message = $"Loading chain at height {height}.", State = FullNodeState.Initializing.ToString() });

                        this.logger.Info($"Loading chain at height {height}.");
                    }

                    height++;
                }

                if (tip == null)
                {
                    genesisHeader.SetChainStore(this.chainStore);
                    tip = genesisHeader;
                }
                else
                {
                    // Confirm that the chain tip exists in the headers table.
                    this.chainStore.GetHeader(tip, tip.HashBlock);
                }

                this.locator = tip.GetLocator();
                return tip;
            });

            return task;
        }

        /// <inheritdoc />
        public Task SaveAsync(ChainIndexer chainIndexer)
        {
            Guard.NotNull(chainIndexer, nameof(chainIndexer));

            Task task = Task.Run(() =>
            {
                ChainedHeader fork = this.locator == null ? null : chainIndexer.FindFork(this.locator);
                ChainedHeader tip = chainIndexer.Tip;
                ChainedHeader toSave = tip;

                var headers = new List<ChainedHeader>();
                while (toSave != fork)
                {
                    headers.Add(toSave);
                    toSave = toSave.Previous;
                }

                var items = headers.OrderBy(b => b.Height).Select(h => new ChainDataItem
                {
                    Height = h.Height,
                    Data = new ChainData { Hash = h.HashBlock, Work = h.ChainWorkBytes }
                });

                this.chainStore.PutChainData(items);

                this.locator = tip.GetLocator();
            });

            return task;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            (this.chainStore as IDisposable)?.Dispose();
        }

        public class ChainRepositoryData : IBitcoinSerializable
        {
            private uint256 hash;
            private byte[] work;

            public uint256 Hash => this.hash;

            public byte[] Work => this.work;

            public ChainRepositoryData()
            {
            }

            public ChainRepositoryData(uint256 hash, byte[] work) : this()
            {
                this.hash = hash;
                this.work = work;
            }

            public void ReadWrite(BitcoinStream stream)
            {
                stream.ReadWrite(ref this.hash);
                if (stream.Serializing)
                {
                    int len = this.work.Length;
                    stream.ReadWrite(ref len);
                    stream.ReadWrite(ref this.work);
                }
                else
                {
                    int len = 0;
                    stream.ReadWrite(ref len);
                    this.work = new byte[len];
                    stream.ReadWrite(ref this.work);
                }
            }
        }
    }
}
