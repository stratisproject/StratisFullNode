using System;
using System.Collections.Generic;
using LevelDB;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Consensus
{
    public class LeveldbHeaderStore : IChainStore, IDisposable
    {
        private readonly Network network;

        internal static readonly byte ChainTableName = 1;
        internal static readonly byte HeaderTableName = 2;

        /// <summary>
        /// Headers that are close to the tip
        /// </summary>
        private readonly MemoryCountCache<uint256, BlockHeader> headers;

        private readonly DB leveldb;

        private object locker;

        public LeveldbHeaderStore(Network network, DataFolder dataFolder, ChainIndexer chainIndexer)
        {
            this.network = network;
            this.ChainIndexer = chainIndexer;
            this.headers = new MemoryCountCache<uint256, BlockHeader>(601);
            this.locker = new object();

            // Open a connection to a new DB and create if not found
            var options = new Options { CreateIfMissing = true };
            this.leveldb = new DB(options, dataFolder.ChainPath);
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
                bytes = this.leveldb.Get(HeaderTableName, bytes);
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
                newHeader.Bits = blockHeader.Bits;
                newHeader.Time = blockHeader.Time;
                newHeader.Nonce = blockHeader.Nonce;
                newHeader.Version = blockHeader.Version;
                newHeader.HashMerkleRoot = blockHeader.HashMerkleRoot;
                newHeader.HashPrevBlock = blockHeader.HashPrevBlock;

                blockHeader = newHeader;
            }

            lock (this.locker)
            {
                this.leveldb.Put(HeaderTableName, blockHeader.GetHash().ToBytes(), blockHeader.ToBytes(consensusFactory));
            }

            return true;
        }

        public ChainData GetChainData(int height)
        {
            byte[] bytes = null;

            lock (this.locker)
            {
                bytes = this.leveldb.Get(ChainTableName, BitConverter.GetBytes(height));
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
                    this.leveldb.Write(batch, new WriteOptions { Sync = true });
                }
            }
        }

        public void Dispose()
        {
            this.leveldb?.Dispose();
        }
    }
}
