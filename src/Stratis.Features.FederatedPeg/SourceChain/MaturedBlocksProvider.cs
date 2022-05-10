using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
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
        /// Retrieves all deposits maturing from the specified height onwards. 
        /// Stops returning deposits once a time limit is reached but the client can resume by calling the method again.
        /// </summary>
        /// <param name="maturityHeight">Deposits maturing from this height onwards will be returned.</param>
        /// 
        /// <returns>A list of mature block deposits.</returns>
        Task<SerializableResult<List<MaturedBlockDepositsModel>>> RetrieveDepositsAsync(int maturityHeight);

        /// <summary>
        /// Retrieves the list of maturing deposits from the cache (if available).
        /// </summary>
        /// <param name="maxToReturn">The maximum number of deposits to return.</param>
        /// <returns>A list of maturing deposits ordered by first maturing.</returns>
        (int blocksBeforeMature, IDeposit deposit)[] GetMaturingDeposits(int maxToReturn);
    }

    public sealed class BlockDeposits
    {
        public uint256 BlockHash { get; set; }

        public IReadOnlyList<IDeposit> Deposits { get; set; }
    }

    public sealed class MaturedBlocksProvider : IMaturedBlocksProvider
    {
        public const int MaturedBlocksBatchSize = 100;

        private readonly IConsensusManager consensusManager;
        private readonly IDepositExtractor depositExtractor;
        private readonly ConcurrentDictionary<int, BlockDeposits> deposits;
        private readonly ILogger logger;
        private readonly IRetrievalTypeConfirmations retrievalTypeConfirmations;

        public MaturedBlocksProvider(IConsensusManager consensusManager, IDepositExtractor depositExtractor, IRetrievalTypeConfirmations retrievalTypeConfirmations)
        {
            this.consensusManager = consensusManager;
            this.depositExtractor = depositExtractor;
            this.retrievalTypeConfirmations = retrievalTypeConfirmations;
            this.logger = LogManager.GetCurrentClassLogger();

            // Take a copy of the tip upfront so that we work with the same tip later.
            this.deposits = new ConcurrentDictionary<int, BlockDeposits>();
        }

        /// <inheritdoc />
        public async Task<SerializableResult<List<MaturedBlockDepositsModel>>> RetrieveDepositsAsync(int maturityHeight)
        {
            if (this.consensusManager.Tip == null)
                return SerializableResult<List<MaturedBlockDepositsModel>>.Fail("Consensus is not ready to provide blocks (it is un-initialized or still starting up).");

            var result = new SerializableResult<List<MaturedBlockDepositsModel>>
            {
                Value = new List<MaturedBlockDepositsModel>(),
                Message = ""
            };

            // If we're asked for blocks beyond the tip then let the caller know that there are no new blocks available.
            if (maturityHeight > this.consensusManager.Tip.Height)
                return result;

            int maxConfirmations = this.retrievalTypeConfirmations.MaximumConfirmationsAtMaturityHeight(maturityHeight);
            int startHeight = maturityHeight - maxConfirmations;

            // Determine the first block to extract deposits for.
            ChainedHeader firstToProcess = this.consensusManager.Tip.GetAncestor(maturityHeight);
            for (ChainedHeader verifyBlock = firstToProcess?.Previous; verifyBlock != null && verifyBlock.Height >= startHeight; verifyBlock = verifyBlock.Previous)
            {
                if (!this.deposits.TryGetValue(verifyBlock.Height, out BlockDeposits blockDeposits) || blockDeposits.BlockHash != verifyBlock.HashBlock)
                    firstToProcess = verifyBlock;
            }

            var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(RestApiClientBase.TimeoutSeconds / 2));

            // Process the blocks after the previous block until the last available block or time expires.
            foreach (ChainedHeaderBlock chainedHeaderBlock in this.consensusManager.GetBlocksAfterBlock(firstToProcess?.Previous, MaturedBlocksBatchSize, cancellationToken))
            {
                // Find all deposits in the given block.
                await RecordBlockDepositsAsync(chainedHeaderBlock, this.retrievalTypeConfirmations).ConfigureAwait(false);

                // Don't process blocks below the requested maturity height.
                if (chainedHeaderBlock.ChainedHeader.Height < maturityHeight)
                {
                    this.logger.LogDebug("{0} below maturity height of {1}.", chainedHeaderBlock.ChainedHeader, maturityHeight);
                    continue;
                }

                var maturedDeposits = new List<IDeposit>();

                // Inspect the deposits in the block for each retrieval type (validate against the retrieval type's confirmation requirement).
                foreach ((DepositRetrievalType retrievalType, int requiredConfirmations) in this.retrievalTypeConfirmations)
                {
                    // If the block height is more than the required confirmations, then the potential deposits
                    // contained within are valid for the given retrieval type.
                    if (chainedHeaderBlock.ChainedHeader.Height > requiredConfirmations)
                        maturedDeposits.AddRange(this.RecallBlockDeposits(chainedHeaderBlock.ChainedHeader.Height - requiredConfirmations, retrievalType));
                }

                this.logger.LogDebug("{0} mature deposits retrieved from block '{1}'.", maturedDeposits.Count, chainedHeaderBlock.ChainedHeader);

                result.Value.Add(new MaturedBlockDepositsModel(new MaturedBlockInfoModel()
                {
                    BlockHash = chainedHeaderBlock.ChainedHeader.HashBlock,
                    BlockHeight = chainedHeaderBlock.ChainedHeader.Height,
                    BlockTime = chainedHeaderBlock.ChainedHeader.Header.Time
                }, maturedDeposits));

                // Clean-up.
                this.deposits.TryRemove(chainedHeaderBlock.ChainedHeader.Height - maxConfirmations, out _);
            }

            return result;
        }

        public (int blocksBeforeMature, IDeposit deposit)[] GetMaturingDeposits(int maxToReturn)
        {
            ChainedHeader tip = this.consensusManager.Tip;

            int maxConfirmations = this.retrievalTypeConfirmations.MaximumConfirmationsAtMaturityHeight(tip.Height);

            var deposits = new SortedDictionary<int, List<IDeposit>>();

            for (int offset = -maxConfirmations; offset < 0; offset++)
            {
                if (this.deposits.TryGetValue(tip.Height + offset, out BlockDeposits blockDeposits))
                {
                    foreach (IDeposit deposit in blockDeposits.Deposits)
                    {
                        int blocksBeforeMature = (deposit.BlockNumber + this.retrievalTypeConfirmations.GetDepositConfirmations(deposit.BlockNumber, deposit.RetrievalType)) - tip.Height;
                        if (blocksBeforeMature >= 0)
                        {
                            if (!deposits.TryGetValue(blocksBeforeMature, out List<IDeposit> depositList))
                            {
                                depositList = new List<IDeposit>();
                                deposits[blocksBeforeMature] = depositList;
                            }

                            depositList.Add(deposit);
                        }
                    }
                }
            }

            return deposits
                .SelectMany(d => d.Value, (beforeMature, deposit) => (beforeMature.Key, deposit))
                .Take(maxToReturn)
                .ToArray();
        }

        private async Task RecordBlockDepositsAsync(ChainedHeaderBlock chainedHeaderBlock, IRetrievalTypeConfirmations retrievalTypes)
        {
            // Already have this recorded?
            if (this.deposits.TryGetValue(chainedHeaderBlock.ChainedHeader.Height, out BlockDeposits blockDeposits) && blockDeposits.BlockHash == chainedHeaderBlock.ChainedHeader.HashBlock)
            {
                this.logger.LogDebug("Deposits already recorded for '{0}'.", chainedHeaderBlock.ChainedHeader);
                return;
            }

            IReadOnlyList<IDeposit> deposits = await this.depositExtractor.ExtractDepositsFromBlock(chainedHeaderBlock.Block, chainedHeaderBlock.ChainedHeader.Height, retrievalTypes).ConfigureAwait(false);

            this.logger.LogDebug("{0} potential deposits extracted from block '{1}'.", deposits.Count, chainedHeaderBlock.ChainedHeader);

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
    /// Conversion deposits are processed after similar intervals to the above, according to their size.
    /// Reward distribution deposits are only processed after the height has increased past max re-org (<see cref="IFederatedPegSettings.MinimumConfirmationsDistributionDeposits"/>) confirmations (blocks).
    /// </summary>
    public enum DepositRetrievalType
    {
        Small,
        Normal,
        Large,
        Distribution,
        ConversionSmall,
        ConversionNormal,
        ConversionLarge
    }
}
