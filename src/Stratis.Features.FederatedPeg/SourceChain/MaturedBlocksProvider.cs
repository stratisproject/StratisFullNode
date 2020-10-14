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
        public const string UnableToRetrieveBlockDataFromConsensusMessage = "Stopping mature block collection and sending what we've collected. Reason: Unable to get block data for {0} from consensus.";

        private readonly IConsensusManager consensusManager;
        private readonly ChainedHeader consensusTip;
        private readonly IDepositExtractor depositExtractor;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILogger logger;
        private readonly ConcurrentDictionary<int, BlockDeposits> deposits;

        public MaturedBlocksProvider(IConsensusManager consensusManager, IDepositExtractor depositExtractor, IFederatedPegSettings federatedPegSettings, ILoggerFactory loggerFactory)
        {
            this.consensusManager = consensusManager;
            this.depositExtractor = depositExtractor;
            this.federatedPegSettings = federatedPegSettings;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            // Take a copy of the tip upfront so that we work with the same tip later.
            this.consensusTip = this.consensusManager.Tip;
            this.deposits = new ConcurrentDictionary<int, BlockDeposits>();
        }

        private void RecordBlockDeposits(ChainedHeaderBlock chainedHeaderBlock, DepositRetrievalType[] retrievalTypes)
        {
            // Already have  this recorded?
            if (this.deposits.TryGetValue(chainedHeaderBlock.ChainedHeader.Height, out BlockDeposits blockDeposits)
                && blockDeposits.BlockHash == chainedHeaderBlock.ChainedHeader.HashBlock)
            {
                return;
            }

            IReadOnlyList<IDeposit> deposits = this.depositExtractor.ExtractDepositsFromBlock(chainedHeaderBlock.Block, chainedHeaderBlock.ChainedHeader.Height, retrievalTypes);

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

        /// <inheritdoc />
        public SerializableResult<List<MaturedBlockDepositsModel>> RetrieveDeposits(int retrieveFromHeight)
        {
            if (this.consensusManager.Tip == null)
                return SerializableResult<List<MaturedBlockDepositsModel>>.Fail("Consensus is not ready to provide blocks (it is un-initialized or still starting up).");

            var result = new SerializableResult<List<MaturedBlockDepositsModel>>
            {
                Value = new List<MaturedBlockDepositsModel>()
            };

            var retrievalTypeConfirmations = new Dictionary<DepositRetrievalType, int>();
            retrievalTypeConfirmations[DepositRetrievalType.Small] = this.federatedPegSettings.MinimumConfirmationsSmallDeposits;
            retrievalTypeConfirmations[DepositRetrievalType.Normal] = this.federatedPegSettings.MinimumConfirmationsNormalDeposits;
            retrievalTypeConfirmations[DepositRetrievalType.Large] = this.federatedPegSettings.MinimumConfirmationsLargeDeposits;

            if (this.federatedPegSettings.IsMainChain)
                retrievalTypeConfirmations[DepositRetrievalType.Distribution] = this.federatedPegSettings.MinimumConfirmationsDistributionDeposits;
            
            int maxConfirmations = retrievalTypeConfirmations.Values.Max();
            ChainedHeader firstBlock = this.consensusManager.Tip.GetAncestor(retrieveFromHeight - maxConfirmations);

            CancellationTokenSource cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(RestApiClientBase.TimeoutSeconds / 2));

            DepositRetrievalType[] retrievalTypes = retrievalTypeConfirmations.Keys.ToArray();

            foreach (ChainedHeaderBlock chb in this.consensusManager.GetBlockDataFrom(firstBlock, cancellationToken))
            {
                this.RecordBlockDeposits(chb, retrievalTypes);

                if (chb.ChainedHeader.Height < retrieveFromHeight)
                    continue;

                var maturedDeposits = new List<IDeposit>();

                foreach ((DepositRetrievalType retrievalType, int requiredConfirmations) in retrievalTypeConfirmations)
                    if (chb.ChainedHeader.Height > requiredConfirmations)
                        foreach (IDeposit deposit in this.RecallBlockDeposits(chb.ChainedHeader.Height - requiredConfirmations, retrievalType))
                            maturedDeposits.Add(deposit);

                result.Value.Add(new MaturedBlockDepositsModel(new MaturedBlockInfoModel() { 
                    BlockHash = chb.ChainedHeader.HashBlock, 
                    BlockHeight = chb.ChainedHeader.Height, 
                    BlockTime = chb.ChainedHeader.Header.Time }, maturedDeposits));

                // Clean-up.
                this.deposits.TryRemove(chb.ChainedHeader.Height - maxConfirmations, out _);
            }

            return result;
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