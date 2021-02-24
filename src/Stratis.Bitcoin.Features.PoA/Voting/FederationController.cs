using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NLog;
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
        private readonly IIdleFederationMembersKicker idleFederationMembersKicker;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly ReconstructFederationService reconstructFederationService;
        private readonly VotingManager votingManager;

        public FederationController(
            ChainIndexer chainIndexer,
            IFederationManager federationManager,
            VotingManager votingManager,
            Network network,
            IIdleFederationMembersKicker idleFederationMembersKicker,
            ReconstructFederationService reconstructFederationService)
        {
            this.chainIndexer = chainIndexer;
            this.federationManager = federationManager;
            this.idleFederationMembersKicker = idleFederationMembersKicker;
            this.network = network;
            this.reconstructFederationService = reconstructFederationService;
            this.votingManager = votingManager;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Signals the node to rebuild the federation via cleabning and rebuilding executed polls.
        /// This will be done via writing a flag to the .conf file so that on startup it be executed.
        /// </summary>
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
                this.logger.Error("Exception occurred: {0}", e.ToString());
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
                    PubKey = this.federationManager.CurrentFederationKey.PubKey
                };

                KeyValuePair<PubKey, uint> lastActive = this.idleFederationMembersKicker.GetFederationMembersByLastActiveTime().FirstOrDefault(x => x.Key == federationMemberModel.PubKey);
                if (lastActive.Key != null)
                {
                    federationMemberModel.LastActiveTime = new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(lastActive.Value);
                    federationMemberModel.PeriodOfInActivity = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(lastActive.Value);
                }

                // Is this member part of a pending poll
                Poll poll = this.votingManager.GetPendingPolls().MemberPolls().OrderByDescending(p => p.PollStartBlockData.Height).FirstOrDefault(p => this.votingManager.GetMemberVotedOn(p.VotingData).PubKey == federationMemberModel.PubKey);
                if (poll != null)
                {
                    federationMemberModel.PollType = poll.VotingData.Key.ToString();
                    federationMemberModel.PollStartBlockHeight = poll.PollStartBlockData.Height;
                    federationMemberModel.PollNumberOfVotesAcquired = poll.PubKeysHexVotedInFavor.Count;
                }

                // Has the poll finished?
                poll = this.votingManager.GetApprovedPolls().MemberPolls().OrderByDescending(p => p.PollVotedInFavorBlockData.Height).FirstOrDefault(p => this.votingManager.GetMemberVotedOn(p.VotingData).PubKey == federationMemberModel.PubKey);
                if (poll != null)
                {
                    federationMemberModel.PollType = poll.VotingData.Key.ToString();
                    federationMemberModel.PollStartBlockHeight = poll.PollStartBlockData.Height;
                    federationMemberModel.PollNumberOfVotesAcquired = poll.PubKeysHexVotedInFavor.Count;
                    federationMemberModel.PollFinishedBlockHeight = poll.PollVotedInFavorBlockData.Height;
                    federationMemberModel.MemberWillStartMiningAtBlockHeight = poll.PollVotedInFavorBlockData.Height + this.network.Consensus.MaxReorgLength;
                    federationMemberModel.MemberWillStartEarningRewardsEstimateHeight = federationMemberModel.MemberWillStartMiningAtBlockHeight + 480;

                    if (this.chainIndexer.Height > poll.PollVotedInFavorBlockData.Height + this.network.Consensus.MaxReorgLength)
                        federationMemberModel.PollWillFinishInBlocks = 0;
                    else
                        federationMemberModel.PollWillFinishInBlocks = (poll.PollVotedInFavorBlockData.Height + this.network.Consensus.MaxReorgLength) - this.chainIndexer.Tip.Height;
                }

                // Has the poll executed?
                poll = this.votingManager.GetExecutedPolls().MemberPolls().OrderByDescending(p => p.PollExecutedBlockData.Height).FirstOrDefault(p => this.votingManager.GetMemberVotedOn(p.VotingData).PubKey == federationMemberModel.PubKey);
                if (poll != null)
                    federationMemberModel.PollExecutedBlockHeight = poll.PollExecutedBlockData.Height;

                federationMemberModel.RewardEstimatePerBlock = 9d / this.federationManager.GetFederationMembers().Count;

                return Json(federationMemberModel);
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
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
                ConcurrentDictionary<PubKey, uint> activeTimes = this.idleFederationMembersKicker.GetFederationMembersByLastActiveTime();
                foreach (IFederationMember federationMember in federationMembers)
                {
                    federationMemberModels.Add(new FederationMemberModel()
                    {
                        PubKey = federationMember.PubKey,
                        CollateralAmount = (federationMember as CollateralFederationMember).CollateralAmount.ToUnit(MoneyUnit.BTC),
                        LastActiveTime = new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(activeTimes.FirstOrDefault(a => a.Key == federationMember.PubKey).Value),
                        PeriodOfInActivity = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(activeTimes.FirstOrDefault(a => a.Key == federationMember.PubKey).Value)
                    });
                }

                return Json(federationMemberModels);
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
