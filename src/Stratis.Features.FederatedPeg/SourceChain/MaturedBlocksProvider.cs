using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public interface IMaturedBlocksProvider
    {
        /// <summary>
        /// Retrieves deposits for the indicated blocks from the block repository and throws an error if the blocks are not mature enough.
        /// </summary>
        /// <param name="retrieveFromHeight">The block height at which to start retrieving blocks.</param>
        /// 
        /// <returns>A list of mature block deposits.</returns>
        SerializableResult<List<MaturedBlockDepositsModel>> RetrieveDeposits(int retrieveFromHeight);
    }

    public sealed class BlockDeposits
    {
        public uint256 BlockHash { get; set; }

        public IReadOnlyList<IDeposit> Deposits { get; set; }
    }

    public sealed class MaturedBlocksProvider : IMaturedBlocksProvider
    {
        public const int MaturedBlocksBatchSize = 100;
        public const string UnableToRetrieveBlockDataFromConsensusMessage = "Stopping mature block collection and sending what we've collected. Reason: Unable to get block data for {0} from consensus.";

        private readonly IConsensusManager consensusManager;
        private readonly IDepositExtractor depositExtractor;
        private readonly ConcurrentDictionary<int, BlockDeposits> deposits;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILogger logger;
        private readonly Dictionary<DepositRetrievalType, int> retrievalTypeConfirmations;

        public MaturedBlocksProvider(IConsensusManager consensusManager, IDepositExtractor depositExtractor, IFederatedPegSettings federatedPegSettings, ILoggerFactory loggerFactory)
        {
            this.consensusManager = consensusManager;
            this.depositExtractor = depositExtractor;
            this.federatedPegSettings = federatedPegSettings;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            // Take a copy of the tip upfront so that we work with the same tip later.
            this.deposits = new ConcurrentDictionary<int, BlockDeposits>();
            this.retrievalTypeConfirmations = new Dictionary<DepositRetrievalType, int>
            {
                [DepositRetrievalType.Small] = this.federatedPegSettings.MinimumConfirmationsSmallDeposits,
                [DepositRetrievalType.Normal] = this.federatedPegSettings.MinimumConfirmationsNormalDeposits,
                [DepositRetrievalType.Large] = this.federatedPegSettings.MinimumConfirmationsLargeDeposits
            };

            if (this.federatedPegSettings.IsMainChain)
                this.retrievalTypeConfirmations[DepositRetrievalType.Distribution] = this.federatedPegSettings.MinimumConfirmationsDistributionDeposits;
        }

        /// <inheritdoc />
        public SerializableResult<List<MaturedBlockDepositsModel>> RetrieveDeposits(int maturityHeight)
        {
            if (this.consensusManager.Tip == null)
                return SerializableResult<List<MaturedBlockDepositsModel>>.Fail("Consensus is not ready to provide blocks (it is un-initialized or still starting up).");

            var result = new SerializableResult<List<MaturedBlockDepositsModel>>
            {
                Value = new List<MaturedBlockDepositsModel>()
            };
            
            int maxConfirmations = this.retrievalTypeConfirmations.Values.Max();
            int startHeight = maturityHeight - maxConfirmations;
            ChainedHeader firstBlock = this.consensusManager.Tip.GetAncestor(maturityHeight);
            firstBlock = firstBlock.EnumerateToGenesis().SkipWhile(h => h.Height > startHeight && (!this.deposits.TryGetValue(h.Height, out BlockDeposits blockDeposits) || blockDeposits.BlockHash != h.HashBlock)).FirstOrDefault();
            var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(RestApiClientBase.TimeoutSeconds / 2));
            DepositRetrievalType[] retrievalTypes = this.retrievalTypeConfirmations.Keys.ToArray();

            foreach (ChainedHeaderBlock chainedHeaderBlock in this.consensusManager.GetBlockDataFromHeight(firstBlock, MaturedBlocksBatchSize, cancellationToken))
            {
                // Find all deposits in the given block.
                RecordBlockDeposits(chainedHeaderBlock, retrievalTypes);

                // Don't process blocks below the requested maturity height.
                if (chainedHeaderBlock.ChainedHeader.Height < maturityHeight)
                {
                    this.logger.LogDebug($"{chainedHeaderBlock.ChainedHeader} below maturity height of {maturityHeight}.");
                    continue;
                }

                var maturedDeposits = new List<IDeposit>();

                // Inspect the deposits in the block for each retrieval type (validate against the retrieval type's confirmation requirement).
                foreach ((DepositRetrievalType retrievalType, int requiredConfirmations) in this.retrievalTypeConfirmations)
                {
                    // If the block height is more than the required confirmations, then the potential deposits
                    // contained within are valid for the given retrieval type.
                    if (chainedHeaderBlock.ChainedHeader.Height > requiredConfirmations)
                    {
                        foreach (IDeposit deposit in this.RecallBlockDeposits(chainedHeaderBlock.ChainedHeader.Height - requiredConfirmations, retrievalType))
                            maturedDeposits.Add(deposit);
                    }
                }

                this.logger.LogDebug($"{maturedDeposits.Count} mature deposits retrieved from block '{chainedHeaderBlock.ChainedHeader}'.");

                result.Value.Add(new MaturedBlockDepositsModel(new MaturedBlockInfoModel()
                {
                    BlockHash = chainedHeaderBlock.ChainedHeader.HashBlock,
                    BlockHeight = chainedHeaderBlock.ChainedHeader.Height,
                    BlockTime = chainedHeaderBlock.ChainedHeader.Header.Time
                }, maturedDeposits));

                // Clean-up.
                this.deposits.TryRemove(chainedHeaderBlock.ChainedHeader.Height - maxConfirmations, out _);
            }

            result.Message = "";

            return result;
        }

        private void RecordBlockDeposits(ChainedHeaderBlock chainedHeaderBlock, DepositRetrievalType[] retrievalTypes)
        {
            // Already have this recorded?
            if (this.deposits.TryGetValue(chainedHeaderBlock.ChainedHeader.Height, out BlockDeposits blockDeposits) && blockDeposits.BlockHash == chainedHeaderBlock.ChainedHeader.HashBlock)
            {
                this.logger.LogDebug($"Deposits already recorded for '{chainedHeaderBlock.ChainedHeader}'.");
                return;
            }

            IReadOnlyList<IDeposit> deposits = this.depositExtractor.ExtractDepositsFromBlock(chainedHeaderBlock.Block, chainedHeaderBlock.ChainedHeader.Height, retrievalTypes);

            this.logger.LogDebug($"{deposits.Count} potential deposits extracted from block '{chainedHeaderBlock.ChainedHeader}'.");

            this.deposits[chainedHeaderBlock.ChainedHeader.Height] = new BlockDeposits()
            {
                BlockHash = chainedHeaderBlock.ChainedHeader.HashBlock,
                Deposits = deposits
            };
        }

        private IEnumerable<IDeposit> RecallBlockDeposits(int blockHeight, DepositRetrievalType retrievalType)
        {
            return this.deposits[blockHeight].Deposits.Where(d => d.RetrievalType == retrievalType);
        }
    }

    /// <summary>
    /// Small deposits are processed after <see cref="IFederatedPegSettings.MinimumConfirmationsSmallDeposits"/> confirmations (blocks).
    /// Normal deposits are processed after (<see cref="IFederatedPegSettings.MinimumConfirmationsNormalDeposits"/>) confirmations (blocks).
    /// Large deposits are only processed after the height has increased past max re-org (<see cref="IFederatedPegSettings.MinimumConfirmationsLargeDeposits"/>) confirmations (blocks).
    /// Similarly, reward distribution deposits are only processed after the height has increased past max re-org (<see cref="IFederatedPegSettings.MinimumConfirmationsDistributionDeposits"/>) confirmations (blocks).
    /// </summary>
    public enum DepositRetrievalType
    {
        Small,
        Normal,
        Large,
        Distribution
    }
}