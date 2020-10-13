﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.TargetChain;

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

    public sealed class MaturedBlocksProvider : IMaturedBlocksProvider
    {
        public const string UnableToRetrieveBlockDataFromConsensusMessage = "Stopping mature block collection and sending what we've collected. Reason: Unable to get block data for {0} from consensus.";

        private readonly IConsensusManager consensusManager;
        private readonly ChainedHeader consensusTip;
        private readonly IDepositExtractor depositExtractor;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILogger logger;

        public MaturedBlocksProvider(IConsensusManager consensusManager, IDepositExtractor depositExtractor, IFederatedPegSettings federatedPegSettings, ILoggerFactory loggerFactory)
        {
            this.consensusManager = consensusManager;
            this.depositExtractor = depositExtractor;
            this.federatedPegSettings = federatedPegSettings;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            // Take a copy of the tip upfront so that we work with the same tip later.
            this.consensusTip = this.consensusManager.Tip;
        }

        /// <inheritdoc />
        public SerializableResult<List<MaturedBlockDepositsModel>> RetrieveDeposits(int retrieveFromHeight)
        {
            if (this.consensusTip == null)
                return SerializableResult<List<MaturedBlockDepositsModel>>.Fail("Consensus is not ready to provide blocks (it is un-initialized or still starting up).");

            var result = new SerializableResult<List<MaturedBlockDepositsModel>>
            {
                Value = new List<MaturedBlockDepositsModel>()
            };

            var messageBuilder = new StringBuilder();

            // Check for distributions first, as they are identified more by their destination than their output size or maturity.
            // They can only occur on the main chain.
            if (this.federatedPegSettings.IsMainChain)
                RetrieveDeposits(DepositRetrievalType.Distribution, retrieveFromHeight, messageBuilder, result);

            RetrieveDeposits(DepositRetrievalType.Small, retrieveFromHeight, messageBuilder, result);
            RetrieveDeposits(DepositRetrievalType.Normal, retrieveFromHeight, messageBuilder, result);
            RetrieveDeposits(DepositRetrievalType.Large, retrieveFromHeight, messageBuilder, result);

            result.Message = messageBuilder.ToString();

            return result;
        }

        private void RetrieveDeposits(DepositRetrievalType retrievalType, int retrieveFromHeight, StringBuilder messageBuilder, SerializableResult<List<MaturedBlockDepositsModel>> result)
        {
            var retrieveUpToHeight = DetermineApplicableRetrievalHeight(retrievalType, retrieveFromHeight, out string message);
            if (retrieveUpToHeight == null)
            {
                this.logger.LogDebug(message);
                messageBuilder.AppendLine(message);
            }
            else
            {
                List<MaturedBlockDepositsModel> deposits = RetrieveDepositsFromHeight(retrievalType, retrieveFromHeight, retrieveUpToHeight.Value);
                if (deposits.Any())
                    result.Value.AddRange(deposits);
            }
        }

        private List<MaturedBlockDepositsModel> RetrieveDepositsFromHeight(DepositRetrievalType retrievalType, int retrieveFromHeight, int retrieveToHeight)
        {
            List<ChainedHeader> applicableHeaders = RetrieveApplicableHeaders(retrieveFromHeight, retrieveToHeight);

            var depositBlockModels = new List<MaturedBlockDepositsModel>();

            // Half of the timeout, we will also need time to convert it to json.
            using (var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(RestApiClientBase.TimeoutSeconds / 2)))
            {
                // Process 100 headers at a time.
                for (int headerIndex = 0; headerIndex < applicableHeaders.Count; headerIndex += 100)
                {
                    List<ChainedHeader> subsetOfHeaders = applicableHeaders.GetRange(headerIndex, Math.Min(100, applicableHeaders.Count - headerIndex));

                    ChainedHeaderBlock[] blocks = this.consensusManager.GetBlockData(subsetOfHeaders.Select(h => h.HashBlock).ToList());

                    foreach (ChainedHeaderBlock chainedHeaderBlock in blocks)
                    {
                        if (chainedHeaderBlock?.Block?.Transactions == null)
                        {
                            this.logger.LogDebug(UnableToRetrieveBlockDataFromConsensusMessage, chainedHeaderBlock.ChainedHeader);
                            break;
                        }

                        MaturedBlockDepositsModel depositBlockModel = this.depositExtractor.ExtractBlockDeposits(chainedHeaderBlock, retrievalType);

                        if (depositBlockModel != null && depositBlockModel.Deposits != null)
                            this.logger.LogDebug("{0} '{1}' deposits extracted at block '{2}'", depositBlockModel.Deposits.Count, retrievalType, chainedHeaderBlock.ChainedHeader);

                        depositBlockModels.Add(depositBlockModel);

                        if (depositBlockModels.Count >= MaturedBlocksSyncManager.MaxBlocksToRequest)
                        {
                            this.logger.LogDebug("Stopping matured blocks collection, max block thresholds reached; {0}={1}", nameof(depositBlockModels), depositBlockModels.Count);
                            break;
                        }

                        IEnumerable<MaturedBlockDepositsModel> blocksWithDeposits = depositBlockModels.Where(d => d != null);
                        if (blocksWithDeposits.Any() && blocksWithDeposits.Where(d => d.Deposits != null).SelectMany(d => d.Deposits).Count() >= int.MaxValue)
                        {
                            this.logger.LogDebug("Stopping matured blocks collection, deposit thresholds reached; numberOfDeposits={0}", blocksWithDeposits.SelectMany(d => d.Deposits).Count());
                            break;
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            this.logger.LogDebug("Stopping matured blocks collection, the request is taking too long, sending what has been collected.");
                            break;
                        }
                    }
                }
            }

            return depositBlockModels;
        }

        private List<ChainedHeader> RetrieveApplicableHeaders(int retrieveFromHeight, int retrieveToHeight)
        {
            int maxBlockHeight = Math.Min(retrieveToHeight, retrieveFromHeight + MaturedBlocksSyncManager.MaxBlocksToRequest - 1);

            var headers = new List<ChainedHeader>();
            ChainedHeader header = this.consensusTip.GetAncestor(maxBlockHeight);
            for (int i = maxBlockHeight; i >= retrieveFromHeight; i--)
            {
                headers.Add(header);
                header = header.Previous;
            }

            headers.Reverse();

            return headers;
        }

        private int? DetermineApplicableRetrievalHeight(DepositRetrievalType retrievalType, int retrieveFromHeight, out string message)
        {
            message = string.Empty;

            int applicableMaturityHeight;
            if (retrievalType == DepositRetrievalType.Small)
                applicableMaturityHeight = this.consensusTip.Height - this.federatedPegSettings.MinimumConfirmationsSmallDeposits;
            else if (retrievalType == DepositRetrievalType.Normal)
                applicableMaturityHeight = this.consensusTip.Height - this.federatedPegSettings.MinimumConfirmationsNormalDeposits;
            else if (retrievalType == DepositRetrievalType.Distribution)
                applicableMaturityHeight = this.consensusTip.Height - this.federatedPegSettings.MinimumConfirmationsDistributionDeposits;
            else
                applicableMaturityHeight = this.consensusTip.Height - this.federatedPegSettings.MinimumConfirmationsLargeDeposits;

            if (retrieveFromHeight > applicableMaturityHeight)
            {
                message = string.Format("The submitted block height of {0} is not mature enough for '{1}' deposits, blocks below {2} can be returned.", retrieveFromHeight, retrievalType, applicableMaturityHeight);
                return null;
            }

            this.logger.LogDebug("Blocks will be inspected for '{0}' deposits from height {1}.", retrievalType, applicableMaturityHeight);

            return applicableMaturityHeight;
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