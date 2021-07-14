using System;
using System.Collections.Generic;
using NBitcoin;
using RocksDbSharp;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Persistence.ChainStores
{
    /// <summary>
    /// Rocksdb implementation of the chain storage
    /// </summary>
    public sealed class RocksDbChainStore : IChainStore
    {
        internal static readonly byte ChainTableName = 1;
        internal static readonly byte HeaderTableName = 2;

        private readonly string dataFolder;
        private readonly Network network;

        /// <summary> Headers that are close to the tip. </summary>
        private readonly MemoryCountCache<uint256, BlockHeader> headers;

        private readonly object locker;
        private readonly DbOptions dbOptions;
        private readonly RocksDb rocksDb;

        public RocksDbChainStore(Network network, DataFolder dataFolder, ChainIndexer chainIndexer)
        {
            this.dataFolder = dataFolder.ChainPath;
            this.network = network;
            this.ChainIndexer = chainIndexer;
            this.headers = new MemoryCountCache<uint256, BlockHeader>(601);
            this.locker = new object();

            this.dbOptions = new DbOptions().SetCreateIfMissing(true);
            this.rocksDb = RocksDb.Open(this.dbOptions, this.dataFolder);
        }

        public ChainIndexer ChainIndexer { get; }

        public BlockHeader GetHeader(ChainedHeader chainedHeader, uint256 hash)
        {
            if (this.headers.TryGetValue(hash, out BlockHeader blockHeader))
            {
                return blockHeader;
            }

            // TODO: Bring in uint256 span optimisations
            byte[] bytes = hash.ToBytes();

            lock (this.locker)
            {
                bytes = this.rocksDb.Get(HeaderTableName, bytes);
            }

            if (bytes == null)
            {
                throw new ApplicationException("Header must exist if requested");
            }

            blockHeader = this.network.Consensus.ConsensusFactory.CreateBlockHeader();
            blockHeader.FromBytes(bytes, this.network.Consensus.ConsensusFactory);

            // If the header is 500 blocks behind tip or 100 blocks ahead cache it.
            if ((chainedHeader.Height > this.ChainIndexer.Height - 500) && (chainedHeader.Height <= this.ChainIndexer.Height + 100))
                this.headers.AddOrUpdate(hash, blockHeader);

            return blockHeader;
        }

        public bool PutHeader(BlockHeader blockHeader)
        {
            ConsensusFactory consensusFactory = this.network.Consensus.ConsensusFactory;

            if (blockHeader is ProvenBlockHeader)
            {
                // If ProvenBlockHeader copy the header parameters.
                BlockHeader newHeader = consensusFactory.CreateBlockHeader();
                newHeader.CopyFields(blockHeader);

                blockHeader = newHeader;
            }

            lock (this.locker)
            {
                this.rocksDb.Put(HeaderTableName, blockHeader.GetHash().ToBytes(), blockHeader.ToBytes(consensusFactory));
            }

            return true;
        }

        public ChainData GetChainData(int height)
        {
            byte[] bytes = null;

            lock (this.locker)
            {
                bytes = this.rocksDb.Get(ChainTableName, BitConverter.GetBytes(height));
            }

            if (bytes == null)
            {
                return null;
            }

            var data = new ChainData();
            data.FromBytes(bytes, this.network.Consensus.ConsensusFactory);

            return data;
        }

        public void PutChainData(IEnumerable<ChainDataItem> items)
        {
            using (var batch = new WriteBatch())
            {
                foreach (var item in items)
                {
                    batch.Put(ChainTableName, BitConverter.GetBytes(item.Height), item.Data.ToBytes(this.network.Consensus.ConsensusFactory));
                }

                lock (this.locker)
                {
                    this.rocksDb.Write(batch);
                }
            }
        }

        public void Dispose()
        {
            this.rocksDb?.Dispose();
        }
    }
}