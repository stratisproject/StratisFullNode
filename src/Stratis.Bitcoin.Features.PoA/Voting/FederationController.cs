using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.PoA.Models;
using Stratis.Bitcoin.Utilities.JsonErrors;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public sealed class FederationController : Controller
    {
        private readonly ChainIndexer chainIndexer;
        private readonly IFederationManager federationManager;
        private readonly IFederationHistory federationHistory;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IPoAMiner poaMiner;
        private readonly ReconstructFederationService reconstructFederationService;
        private readonly VotingManager votingManager;

        public FederationController(
            ChainIndexer chainIndexer,
            IFederationManager federationManager,
            VotingManager votingManager,
            Network network,
            IFederationHistory federationHistory,
            ReconstructFederationService reconstructFederationService,
            IPoAMiner poAMiner = null)
        {
            this.chainIndexer = chainIndexer;
            this.federationManager = federationManager;
            this.federationHistory = federationHistory;
            this.network = network;
            this.poaMiner = poAMiner;
            this.reconstructFederationService = reconstructFederationService;
            this.votingManager = votingManager;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Signals the node to rebuild the federation via cleabning and rebuilding executed polls.
        /// This will be done via writing a flag to the .conf file so that on startup it be executed.
        /// </summary>
        /// <returns>See response codes</returns>
        /// <response code="200">If the reconstruct flag has been set.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("reconstruct")]
        [HttpPut]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult Reconstruct()
        {
            try
            {
                this.reconstructFederationService.SetReconstructionFlag(true);

                return Json("Reconstruction flag set, please restart the node.");
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves information for the current federation member's voting status and mining estimates.
        /// </summary>
        /// <returns>Active federation members</returns>
        /// <response code="200">Returns the active members</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("members/current")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetCurrentMemberInfo()
        {
            try
            {
                if (this.federationManager.CurrentFederationKey == null)
                    throw new Exception("Your node is not registered as a federation member.");

                var federationMemberModel = new FederationMemberDetailedModel
                {
                    PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex()
                };

                ChainedHeader chainTip = this.chainIndexer.Tip;
                federationMemberModel.FederationSize = this.federationHistory.GetFederationForBlock(chainTip).Count;

                KeyValuePair<IFederationMember, uint> lastActive = this.federationHistory.GetFederationMembersByLastActiveTime().FirstOrDefault(x => x.Key.PubKey == this.federationManager.CurrentFederationKey.PubKey);
                if (lastActive.Key != null)
                {
                    federationMemberModel.LastActiveTime = new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(lastActive.Value);
                    federationMemberModel.PeriodOfInActivity = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(lastActive.Value);
                }

                // Is this member part of a pending poll
                Poll poll = this.votingManager.GetPendingPolls().MemberPolls().OrderByDescending(p => p.PollStartBlockData.Height).FirstOrDefault(p => this.votingManager.GetMemberVotedOn(p.VotingData).PubKey == this.federationManager.CurrentFederationKey.PubKey);
                if (poll != null)
                {
                    federationMemberModel.PollType = poll.VotingData.Key.ToString();
                    federationMemberModel.PollStartBlockHeight = poll.PollStartBlockData.Height;
                    federationMemberModel.PollNumberOfVotesAcquired = poll.PubKeysHexVotedInFavor.Count;
                }

                // Has the poll finished?
                poll = this.votingManager.GetApprovedPolls().MemberPolls().OrderByDescending(p => p.PollVotedInFavorBlockData.Height).FirstOrDefault(p => this.votingManager.GetMemberVotedOn(p.VotingData).PubKey == this.federationManager.CurrentFederationKey.PubKey);
                if (poll != null)
                {
                    federationMemberModel.PollType = poll.VotingData.Key.ToString();
                    federationMemberModel.PollStartBlockHeight = poll.PollStartBlockData.Height;
                    federationMemberModel.PollNumberOfVotesAcquired = poll.PubKeysHexVotedInFavor.Count;
                    federationMemberModel.PollFinishedBlockHeight = poll.PollVotedInFavorBlockData.Height;
                    federationMemberModel.MemberWillStartMiningAtBlockHeight = poll.PollVotedInFavorBlockData.Height + this.network.Consensus.MaxReorgLength;
                    federationMemberModel.MemberWillStartEarningRewardsEstimateHeight = federationMemberModel.MemberWillStartMiningAtBlockHeight + 480;

                    if (chainTip.Height > poll.PollVotedInFavorBlockData.Height + this.network.Consensus.MaxReorgLength)
                        federationMemberModel.PollWillFinishInBlocks = 0;
                    else
                        federationMemberModel.PollWillFinishInBlocks = (poll.PollVotedInFavorBlockData.Height + this.network.Consensus.MaxReorgLength) - chainTip.Height;
                }

                // Has the poll executed?
                poll = this.votingManager.GetExecutedPolls().MemberPolls().OrderByDescending(p => p.PollExecutedBlockData.Height).FirstOrDefault(p => this.votingManager.GetMemberVotedOn(p.VotingData).PubKey == this.federationManager.CurrentFederationKey.PubKey);
                if (poll != null)
                    federationMemberModel.PollExecutedBlockHeight = poll.PollExecutedBlockData.Height;

                federationMemberModel.RewardEstimatePerBlock = 9d / this.federationManager.GetFederationMembers().Count;

                federationMemberModel.MiningStatistics = this.poaMiner.MiningStatistics;

                return Json(federationMemberModel);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves a list of active federation members and their last active times.
        /// </summary>
        /// <returns>Active federation members</returns>
        /// <response code="200">Returns the active members</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("members")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetMembers()
        {
            try
            {
                List<IFederationMember> federationMembers = this.federationManager.GetFederationMembers();

                var federationMemberModels = new List<FederationMemberModel>();

                // Get their last active times.
                ConcurrentDictionary<IFederationMember, uint> activeTimes = this.federationHistory.GetFederationMembersByLastActiveTime();
                foreach (IFederationMember federationMember in federationMembers)
                {
                    federationMemberModels.Add(new FederationMemberModel()
                    {
                        PubKey = federationMember.PubKey.ToHex(),
                        CollateralAmount = (federationMember as CollateralFederationMember).CollateralAmount.ToUnit(MoneyUnit.BTC),
                        LastActiveTime = new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(activeTimes.FirstOrDefault(a => a.Key.PubKey == federationMember.PubKey).Value),
                        PeriodOfInActivity = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(activeTimes.FirstOrDefault(a => a.Key.PubKey == federationMember.PubKey).Value)
                    });
                }

                return Json(federationMemberModels);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves the pubkey of the federation member that produced a block at the specified height.
        /// </summary>
        /// <param name="blockHeight">Block height at which to retrieve pubkey from.</param>
        /// <returns>Pubkey of federation member at specified height</returns>
        /// <response code="200">Returns pubkey of miner at block height</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("mineratheight")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetPubkeyAtHeight([FromQuery(Name = "blockHeight")] int blockHeight)
        {
            try
            {
                ChainedHeader chainedHeader = this.chainIndexer.GetHeader(blockHeight);
                PubKey pubKey = this.federationHistory.GetFederationMemberForBlock(chainedHeader)?.PubKey;

                return Json(pubKey);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves federation members at the given height.
        /// </summary>
        /// <param name="blockHeight">Block height at which to retrieve federation membership.</param>
        /// <returns>Federation membership at the given height</returns>
        /// <response code="200">Returns a list of pubkeys representing the federation membership at the given block height.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("federationatheight")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetFederationAtHeight([FromQuery(Name = "blockHeight")] int blockHeight)
        {
            try
            {
                ChainedHeader chainedHeader = this.chainIndexer.GetHeader(blockHeight);
                List<IFederationMember> federationMembers = this.federationHistory.GetFederationForBlock(chainedHeader);
                List<PubKey> federationPubKeys = new List<PubKey>();

                foreach (IFederationMember federationMember in federationMembers)
                {
                    federationPubKeys.Add(federationMember.PubKey);
                }

                return Json(federationPubKeys);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
