using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LevelDB;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Consensus.ProvenBlockHeaders
{
    /// <summary>
    /// Persistent implementation of the <see cref="ProvenBlockHeader"/> DBreeze repository.
    /// </summary>
    public class LevelDbProvenBlockHeaderRepository : IProvenBlockHeaderRepository
    {
        /// <summary>
        /// Instance logger.
        /// </summary>
        private readonly ILogger logger;

        /// <summary>
        /// Access to database.
        /// </summary>
        private readonly DB leveldb;

        private readonly object locker;

        /// <summary>
        /// Specification of the network the node runs on - RegTest/TestNet/MainNet.
        /// </summary>
        private readonly Network network;

        private readonly DBreezeSerializer dBreezeSerializer;

        /// <inheritdoc />
        public HashHeightPair TipHashHeight { get; private set; }

        /// <summary>
        /// Initializes a new instance of the object.
        /// </summary>
        /// <param name="network">Specification of the network the node runs on - RegTest/TestNet/MainNet.</param>
        /// <param name="folder"><see cref="LevelDbProvenBlockHeaderRepository"/> folder path to the DBreeze database files.</param>
        /// <param name="loggerFactory">Factory to create a logger for this type.</param>
        /// <param name="dBreezeSerializer">The serializer to use for <see cref="IBitcoinSerializable"/> objects.</param>
        public LevelDbProvenBlockHeaderRepository(Network network, DataFolder folder, ILoggerFactory loggerFactory,
            DBreezeSerializer dBreezeSerializer)
        : this(network, folder.ProvenBlockHeaderPath, loggerFactory, dBreezeSerializer)
        {
        }

        /// <summary>
        /// Initializes a new instance of the object.
        /// </summary>
        /// <param name="network">Specification of the network the node runs on - RegTest/TestNet/MainNet.</param>
        /// <param name="folder"><see cref="LevelDbProvenBlockHeaderRepository"/> folder path to the DBreeze database files.</param>
        /// <param name="loggerFactory">Factory to create a logger for this type.</param>
        /// <param name="dBreezeSerializer">The serializer to use for <see cref="IBitcoinSerializable"/> objects.</param>
        public LevelDbProvenBlockHeaderRepository(Network network, string folder, ILoggerFactory loggerFactory,
            DBreezeSerializer dBreezeSerializer)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(folder, nameof(folder));
            this.dBreezeSerializer = dBreezeSerializer;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            Directory.CreateDirectory(folder);

            // Open a connection to a new DB and create if not found
            var options = new Options { CreateIfMissing = true };
            this.leveldb = new DB(options, folder);
            this.locker = new object();

            this.network = network;
        }

        /// <inheritdoc />
        public Task InitializeAsync()
        {
            Task task = Task.Run(() =>
            {
                this.TipHashHeight = this.GetTipHash();

                if (this.TipHashHeight != null)
                    return;

                var hashHeight = new HashHeightPair(this.network.GetGenesis().GetHash(), 0);

                this.SetTip(hashHeight);

                this.TipHashHeight = hashHeight;
            });

            return task;
        }

        /// <inheritdoc />
        public Task<ProvenBlockHeader> GetAsync(int blockHeight)
        {
            Task<ProvenBlockHeader> task = Task.Run(() =>
            {
                byte[] row = null;

                lock (this.locker)
                {
                    row = this.leveldb.Get(BlockHeaderRepositoryConstants.ProvenBlockHeaderTable, BitConverter.GetBytes(blockHeight));
                }

                if (row != null)
                    return this.dBreezeSerializer.Deserialize<ProvenBlockHeader>(row);

                return null;
            });

            return task;
        }

        /// <inheritdoc />
        public Task PutAsync(SortedDictionary<int, ProvenBlockHeader> headers, HashHeightPair newTip)
        {
            Guard.NotNull(headers, nameof(headers));
            Guard.NotNull(newTip, nameof(newTip));

            Guard.Assert(newTip.Hash == headers.Values.Last().GetHash());

            Task task = Task.Run(() =>
            {
                this.logger.LogDebug("({0}.Count():{1})", nameof(headers), headers.Count());

                this.InsertHeaders(headers);

                this.SetTip(newTip);

                this.TipHashHeight = newTip;
            });

            return task;
        }

        /// <summary>
        /// Set's the hash and height tip of the new <see cref="ProvenBlockHeader"/>.
        /// </summary>
        /// <param name="newTip"> Hash height pair of the new block tip.</param>
        private void SetTip(HashHeightPair newTip)
        {
            Guard.NotNull(newTip, nameof(newTip));

            lock (this.locker)
            {
                this.leveldb.Put(BlockHeaderRepositoryConstants.BlockHashHeightTable, BlockHeaderRepositoryConstants.BlockHashHeightKey, this.dBreezeSerializer.Serialize(newTip));
            }
        }

        /// <summary>
        /// Inserts <see cref="ProvenBlockHeader"/> items into to the database.
        /// </summary>
        /// <param name="headers"> List of <see cref="ProvenBlockHeader"/> items to save.</param>
        private void InsertHeaders(SortedDictionary<int, ProvenBlockHeader> headers)
        {
            using (var batch = new WriteBatch())
            {
                foreach (KeyValuePair<int, ProvenBlockHeader> header in headers)
                    batch.Put(BlockHeaderRepositoryConstants.ProvenBlockHeaderTable, BitConverter.GetBytes(header.Key), this.dBreezeSerializer.Serialize(header.Value));

                lock (this.locker)
                {
                    this.leveldb.Write(batch, new WriteOptions() { Sync = true });
                }
            }
        }

        /// <summary>
        /// Retrieves the current <see cref="HashHeightPair"/> tip from disk.
        /// </summary>
        /// <returns> Hash of blocks current tip.</returns>
        private HashHeightPair GetTipHash()
        {
            HashHeightPair tipHash = null;

            byte[] row = null;
            lock (this.locker)
            {
                row = this.leveldb.Get(BlockHeaderRepositoryConstants.BlockHashHeightTable, BlockHeaderRepositoryConstants.BlockHashHeightKey);
            }

            if (row != null)
                tipHash = this.dBreezeSerializer.Deserialize<HashHeightPair>(row);

            return tipHash;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.leveldb?.Dispose();
        }
    }
}
