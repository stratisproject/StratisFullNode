using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.BlockStore.Pruning
{
    /// <inheritdoc />
    public class PrunedBlockRepository : IPrunedBlockRepository
    {
        private readonly IBlockRepository blockRepository;
        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly ILogger logger;
        private static readonly byte[] prunedTipKey = new byte[2];
        private readonly StoreSettings storeSettings;

        /// <inheritdoc />
        public HashHeightPair PrunedTip { get; private set; }

        public PrunedBlockRepository(IBlockRepository blockRepository, DBreezeSerializer dBreezeSerializer, ILoggerFactory loggerFactory, StoreSettings storeSettings)
        {
            this.blockRepository = blockRepository;
            this.dBreezeSerializer = dBreezeSerializer;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.storeSettings = storeSettings;
        }

        /// <inheritdoc />
        public void Initialize()
        {
            this.LoadPrunedTip();
        }

        /// <inheritdoc />
        public void PruneAndCompactDatabase(ChainedHeader blockRepositoryTip, Network network, bool nodeInitializing)
        {
            this.logger.LogInformation($"Pruning started...");

            if (this.PrunedTip == null)
            {
                Block genesis = network.GetGenesis();

                this.PrunedTip = new HashHeightPair(genesis.GetHash(), 0);

                this.blockRepository.Put(BlockRepositoryConstants.CommonTableName, prunedTipKey, this.dBreezeSerializer.Serialize(this.PrunedTip));
            }

            if (nodeInitializing)
            {
                if (this.IsDatabasePruned())
                    return;

                this.PrepareDatabaseForCompacting(blockRepositoryTip);
            }

            // LevelDB has its own internal compaction logic

            this.logger.LogInformation($"Pruning complete.");

            return;
        }

        private bool IsDatabasePruned()
        {
            if (this.blockRepository.TipHashAndHeight.Height <= this.PrunedTip.Height + this.storeSettings.AmountOfBlocksToKeep)
            {
                this.logger.LogDebug("(-):true");
                return true;
            }
            else
            {
                this.logger.LogDebug("(-):false");
                return false;
            }
        }

        /// <summary>
        /// Compacts the block and transaction database by recreating the tables without the deleted references.
        /// </summary>
        /// <param name="blockRepositoryTip">The last fully validated block of the node.</param>
        private void PrepareDatabaseForCompacting(ChainedHeader blockRepositoryTip)
        {
            int upperHeight = this.blockRepository.TipHashAndHeight.Height - this.storeSettings.AmountOfBlocksToKeep;

            var toDelete = new List<ChainedHeader>();

            ChainedHeader startFromHeader = blockRepositoryTip.GetAncestor(upperHeight);
            ChainedHeader endAtHeader = blockRepositoryTip.FindAncestorOrSelf(this.PrunedTip.Hash);

            this.logger.LogInformation($"Pruning blocks from height {upperHeight} to {endAtHeader.Height}.");

            while (startFromHeader.Previous != null && startFromHeader != endAtHeader)
            {
                toDelete.Add(startFromHeader);
                startFromHeader = startFromHeader.Previous;
            }

            this.blockRepository.DeleteBlocks(toDelete.Select(cb => cb.HashBlock).ToList());

            this.UpdatePrunedTip(blockRepositoryTip.GetAncestor(upperHeight));
        }

        private void LoadPrunedTip()
        {
            if (this.PrunedTip != null)
                return;

            byte[] row = this.blockRepository.Get(BlockRepositoryConstants.CommonTableName, prunedTipKey);

            if (row != null)
                this.PrunedTip = this.dBreezeSerializer.Deserialize<HashHeightPair>(row);
        }

        /// <inheritdoc />
        public void UpdatePrunedTip(ChainedHeader tip)
        {
            this.PrunedTip = new HashHeightPair(tip);
        }
    }
}
