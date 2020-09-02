using System;
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
            var deposits = new List<MaturedBlockDepositsModel>();

            // Retrieve faster deposits.
            var fasterRetrievalHeight = DetermineApplicableRetrievalHeight(DepositRetrievalType.Faster, retrieveFromHeight, out string message);
            if (fasterRetrievalHeight == null)
                this.logger.LogDebug(message);
            else
            {
                List<MaturedBlockDepositsModel> fasterDeposits = RetrieveDepositsFromHeight(DepositRetrievalType.Faster, fasterRetrievalHeight.Value, retrieveFromHeight);
                if (fasterDeposits.Any())
                    deposits.AddRange(fasterDeposits);
            }

            // Retrieve normal deposits.
            var normalRetrievalHeight = DetermineApplicableRetrievalHeight(DepositRetrievalType.Normal, retrieveFromHeight, out message);
            if (normalRetrievalHeight == null)
                this.logger.LogDebug(message);
            else
            {
                List<MaturedBlockDepositsModel> normalDeposits = RetrieveDepositsFromHeight(DepositRetrievalType.Normal, normalRetrievalHeight.Value, retrieveFromHeight);
                if (normalDeposits.Any())
                    deposits.AddRange(normalDeposits);
            }

            return SerializableResult<List<MaturedBlockDepositsModel>>.Ok(deposits);
        }

        private List<MaturedBlockDepositsModel> RetrieveDepositsFromHeight(DepositRetrievalType retrievalType, int applicableHeight, int retrieveFromHeight)
        {
            List<ChainedHeader> applicableHeaders = RetrieveApplicableHeaders(applicableHeight, retrieveFromHeight);

            var depositBlockModels = new List<MaturedBlockDepositsModel>();

            // Half of the timeout, we will also need time to convert it to json.
            using (var cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(RestApiClientBase.TimeoutSeconds / 2)))
            {
                for (int headerIndex = 0; headerIndex < applicableHeaders.Count; headerIndex += 100)
                {
                    List<ChainedHeader> currentHeaders = applicableHeaders.GetRange(headerIndex, Math.Min(100, applicableHeaders.Count - headerIndex));

                    var hashes = currentHeaders.Select(h => h.HashBlock).ToList();

                    ChainedHeaderBlock[] blocks = this.consensusManager.GetBlockData(hashes);

                    foreach (ChainedHeaderBlock chainedHeaderBlock in blocks)
                    {
                        if (chainedHeaderBlock?.Block?.Transactions == null)
                        {
                            this.logger.LogDebug(UnableToRetrieveBlockDataFromConsensusMessage, chainedHeaderBlock.ChainedHeader);
                            break;
                        }

                        MaturedBlockDepositsModel depositBlockModel = this.depositExtractor.ExtractBlockDeposits(chainedHeaderBlock, retrievalType);

                        if (depositBlockModel.Deposits != null)
                        {
                            this.logger.LogDebug("{0} '{1}' deposits extracted at block '{2}'", depositBlockModel.Deposits.Count, retrievalType, chainedHeaderBlock.ChainedHeader);

                            if (depositBlockModel.Deposits.Any())
                                depositBlockModels.Add(depositBlockModel);
                        }

                        if (depositBlockModels.Count >= MaturedBlocksSyncManager.MaxBlocksToRequest || depositBlockModels.SelectMany(d => d.Deposits).Count() >= int.MaxValue)
                        {
                            this.logger.LogDebug("Stopping matured blocks collection, thresholds reached; {0}={1}, numberOfDeposits={2}", nameof(depositBlockModels), depositBlockModels.Count, depositBlockModels.SelectMany(d => d.Deposits).Count());
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

        private List<ChainedHeader> RetrieveApplicableHeaders(int applicableHeight, int retrieveFromHeight)
        {
            int maxBlockHeight = Math.Min(applicableHeight, retrieveFromHeight + MaturedBlocksSyncManager.MaxBlocksToRequest - 1);

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

            if (this.consensusTip == null)
            {
                message = "Consensus is not ready to provide blocks (it is un-initialized or still starting up).";
                return null;
            }

            int applicableMaturityHeight;
            if (retrievalType == DepositRetrievalType.Faster)
                applicableMaturityHeight = this.consensusTip.Height - this.federatedPegSettings.FasterDepositMinimumConfirmations;
            else
                applicableMaturityHeight = this.consensusTip.Height - this.federatedPegSettings.MinimumDepositConfirmations;

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
    /// Normal deposits are only processed after the height has increased past max re-org (<see cref="IFederatedPegSettings.MinimumDepositConfirmations"/>) confirmations (blocks).
    /// Faster deposits are processed after <see cref="IFederatedPegSettings.FasterDepositMinimumConfirmations"/> confirmations (blocks).
    /// </summary>
    public enum DepositRetrievalType
    {
        Normal,
        Faster
    }
}