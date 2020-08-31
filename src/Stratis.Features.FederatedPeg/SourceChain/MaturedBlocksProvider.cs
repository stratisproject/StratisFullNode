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

namespace Stratis.Features.FederatedPeg.SourceChain
{
    public interface IMaturedBlocksProvider
    {
        /// <summary>
        /// Retrieves deposits for the indicated blocks from the block repository and throws an error if the blocks are not mature enough.
        /// </summary>
        /// <param name="blockHeight">The block height at which to start retrieving blocks.</param>
        /// <param name="maxBlocksToProcess">The number of blocks to retrieve.</param>
        /// <param name="maxDeposits">The number of deposits to retrieve.</param>
        /// <returns>A list of mature block deposits.</returns>
        SerializableResult<List<MaturedBlockDepositsModel>> GetMaturedDeposits(int blockHeight, int maxBlocksToProcess, int maxDeposits = int.MaxValue);

        /// <summary>
        /// Retrieves deposits that are eligible for faster processing.
        /// <para>
        /// Blocks that are considered mature is determined by <see cref="IFederatedPegSettings.MinimumFasterDepositConfirmations"/>.
        /// </para>
        /// </summary>
        /// <param name="blockHeight">The block height at which to start retrieving blocks.</param>
        /// <param name="maxBlocksToProcess">The number of blocks to retrieve.</param>
        /// <param name="maxDeposits">The number of deposits to retrieve.</param>
        /// <returns>A list mature block deposits.</returns>
        SerializableResult<List<MaturedBlockDepositsModel>> GetFasterMaturedDeposits(int blockHeight, int maxBlocksToProcess, int maxDeposits = int.MaxValue);
    }

    public sealed class MaturedBlocksProvider : IMaturedBlocksProvider
    {
        public const string RetrieveBlockHeightHigherThanMaturedTipMessage = "The submitted block height of {0} is not mature enough. Blocks below {1} can be returned.";
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
        public SerializableResult<List<MaturedBlockDepositsModel>> GetMaturedDeposits(int retrieveFromHeight, int maxBlocksToProcess, int maxDepositsToReturn = int.MaxValue)
        {
            (SerializableResult<List<MaturedBlockDepositsModel>> result, int? applicableHeight) = DetermineApplicableBlockMaturityHeight(retrieveFromHeight, (int)this.federatedPegSettings.MinimumDepositConfirmations);
            if (result != null)
                return result;

            List<MaturedBlockDepositsModel> maturedBlockDepositModels = ProcessApplicableHeaders(applicableHeight.Value, retrieveFromHeight, maxBlocksToProcess, maxDepositsToReturn);

            return SerializableResult<List<MaturedBlockDepositsModel>>.Ok(maturedBlockDepositModels);
        }

        /// <inheritdoc />
        public SerializableResult<List<MaturedBlockDepositsModel>> GetFasterMaturedDeposits(int retrieveFromHeight, int maxBlocksToProcess, int maxDepositsToReturn = int.MaxValue)
        {
            (SerializableResult<List<MaturedBlockDepositsModel>> result, int? applicableHeight) = DetermineApplicableBlockMaturityHeight(retrieveFromHeight, (int)this.federatedPegSettings.MinimumFasterDepositConfirmations);
            if (result != null)
                return result;

            List<MaturedBlockDepositsModel> maturedBlockDepositModels = ProcessApplicableHeaders(applicableHeight.Value, retrieveFromHeight, maxBlocksToProcess, maxDepositsToReturn);

            return SerializableResult<List<MaturedBlockDepositsModel>>.Ok(maturedBlockDepositModels);

        }

        private List<MaturedBlockDepositsModel> ProcessApplicableHeaders(int applicableHeight, int retrieveFromHeight, int maxBlocksToProcess, int maxDepositsToReturn)
        {
            List<ChainedHeader> applicableHeadersToProcess = RetrieveApplicableHeadersToProcess(applicableHeight, retrieveFromHeight, maxBlocksToProcess);

            var maturedBlockDepositModels = new List<MaturedBlockDepositsModel>();

            int numberOfDeposits = 0;

            // Half of the timeout, wee will also need time to convert it to json.

            using (var cancellationToken = new CancellationTokenSource(RestApiClientBase.TimeoutSeconds / 2))
            {
                for (int headerIndex = 0; headerIndex < applicableHeadersToProcess.Count; headerIndex += 100)
                {
                    List<ChainedHeader> currentHeaders = applicableHeadersToProcess.GetRange(headerIndex, Math.Min(100, applicableHeadersToProcess.Count - headerIndex));

                    var hashes = currentHeaders.Select(h => h.HashBlock).ToList();

                    ChainedHeaderBlock[] blocks = this.consensusManager.GetBlockData(hashes);

                    foreach (ChainedHeaderBlock chainedHeaderBlock in blocks)
                    {
                        if (chainedHeaderBlock?.Block?.Transactions == null)
                        {
                            this.logger.LogDebug(UnableToRetrieveBlockDataFromConsensusMessage, chainedHeaderBlock.ChainedHeader);
                            break;
                        }

                        MaturedBlockDepositsModel maturedBlockDepositModel = this.depositExtractor.ExtractBlockDeposits(chainedHeaderBlock);

                        if (maturedBlockDepositModel.Deposits != null && maturedBlockDepositModel.Deposits.Count > 0)
                        {
                            this.logger.LogDebug("{0} deposits extracted at block {1}", maturedBlockDepositModel.Deposits.Count, chainedHeaderBlock.ChainedHeader);
                            numberOfDeposits += maturedBlockDepositModel.Deposits.Count();
                            maturedBlockDepositModels.Add(maturedBlockDepositModel);
                        }

                        if (maturedBlockDepositModels.Count >= maxBlocksToProcess || numberOfDeposits >= maxDepositsToReturn)
                        {
                            this.logger.LogDebug("Stopping matured blocks collection, thresholds reached; {0}={1}, {2}={3}", nameof(maturedBlockDepositModels), maturedBlockDepositModels.Count, nameof(numberOfDeposits), numberOfDeposits);
                            break;
                        }

                        if (cancellationToken.IsCancellationRequested)
                        {
                            this.logger.LogDebug("Stopping matured blocks collection, the request is taking too long. Sending what has been collected.");
                            break;
                        }
                    }
                }
            }

            return maturedBlockDepositModels;
        }

        private List<ChainedHeader> RetrieveApplicableHeadersToProcess(int applicableHeight, int retrieveFromHeight, int maxBlocksToProcess)
        {
            int maxBlockHeight = Math.Min(applicableHeight, retrieveFromHeight + maxBlocksToProcess - 1);

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

        private (SerializableResult<List<MaturedBlockDepositsModel>>, int? applicableHeight) DetermineApplicableBlockMaturityHeight(int retrieveFromHeight, int minimumDepositConfirmations)
        {
            if (this.consensusTip == null)
                return (SerializableResult<List<MaturedBlockDepositsModel>>.Fail("Consensus is not ready to provide blocks (un-initialized or still starting up)."), null);

            int applicableMaturityHeight = this.consensusTip.Height - minimumDepositConfirmations;

            if (retrieveFromHeight > applicableMaturityHeight)
            {
                this.logger.LogTrace("(-)[RETRIEVEFROMBLOCK_HIGHER_THAN_MATUREDTIP]:{0}={1},{2}={3}", nameof(retrieveFromHeight), retrieveFromHeight, nameof(applicableMaturityHeight), applicableMaturityHeight);
                return (SerializableResult<List<MaturedBlockDepositsModel>>.Fail(string.Format(RetrieveBlockHeightHigherThanMaturedTipMessage, retrieveFromHeight, applicableMaturityHeight)), null);
            }

            return (null, applicableMaturityHeight);
        }
    }
}