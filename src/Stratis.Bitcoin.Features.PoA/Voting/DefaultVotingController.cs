using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.PoA.Models;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class DefaultVotingController : Controller
    {
        private readonly ChainIndexer chainIndexer;

        protected readonly IFederationManager federationManager;

        private readonly IIdleFederationMembersKicker idleFederationMembersKicker;

        protected readonly ILogger logger;

        protected readonly Network network;

        private readonly IPollResultExecutor pollExecutor;

        protected readonly VotingManager votingManager;

        private readonly IWhitelistedHashesRepository whitelistedHashesRepository;

        public DefaultVotingController(
            ChainIndexer chainIndexer,
            IFederationManager federationManager,
            ILoggerFactory loggerFactory,
            VotingManager votingManager,
            IWhitelistedHashesRepository whitelistedHashesRepository,
            Network network,
            IPollResultExecutor pollExecutor,
            IIdleFederationMembersKicker idleFederationMembersKicker)
        {
            this.chainIndexer = chainIndexer;
            this.federationManager = federationManager;
            this.idleFederationMembersKicker = idleFederationMembersKicker;
            this.network = network;
            this.pollExecutor = pollExecutor;
            this.votingManager = votingManager;
            this.whitelistedHashesRepository = whitelistedHashesRepository;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <summary>
        /// Retrieves information for the current federation member's voting status and mining estimates.
        /// </summary>
        /// <returns>Active federation members</returns>
        /// <response code="200">Returns the active members</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("currentmemberinfo")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult CurrentFederationMemberInfo()
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
        [Route("fedmembers")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetFederationMembers()
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
                        LastActiveTime = new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(activeTimes.FirstOrDefault(a => a.Key == federationMember.PubKey).Value),
                        PeriodOfInActivity = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0).AddSeconds(activeTimes.FirstOrDefault(a => a.Key == federationMember.PubKey).Value)
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
        /// Retrieves a list of active polls.
        /// </summary>
        /// <returns>Active polls</returns>
        /// <response code="200">Returns the active polls</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("polls/pending")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetPendingPolls([FromQuery] VoteKey voteType, [FromQuery] string pubKeyOfMemberBeingVotedOn = "")
        {
            try
            {
                IEnumerable<Poll> polls = this.votingManager.GetPendingPolls().Where(v => v.VotingData.Key == voteType);
                IEnumerable<PollViewModel> models = polls.Select(x => new PollViewModel(x, this.pollExecutor));

                if (!string.IsNullOrEmpty(pubKeyOfMemberBeingVotedOn))
                    models = models.Where(m => m.VotingDataString.Contains(pubKeyOfMemberBeingVotedOn));

                return this.Json(models);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves a list of finished polls.
        /// </summary>
        /// <returns>Finished polls</returns>
        /// <response code="200">Returns the finished polls</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("polls/finished")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetFinishedPolls([FromQuery] VoteKey voteType, [FromQuery] string pubKeyOfMemberBeingVotedOn = "")
        {
            try
            {
                IEnumerable<Poll> polls = this.votingManager.GetApprovedPolls().Where(v => v.VotingData.Key == voteType);
                IEnumerable<PollViewModel> models = polls.Select(x => new PollViewModel(x, this.pollExecutor));

                if (!string.IsNullOrEmpty(pubKeyOfMemberBeingVotedOn))
                    models = models.Where(m => m.VotingDataString.Contains(pubKeyOfMemberBeingVotedOn));

                return this.Json(models);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves a list of executed polls.
        /// </summary>
        /// <returns>Finished polls</returns>
        /// <response code="200">Returns the finished polls</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("polls/executed")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetExecutedPolls([FromQuery] VoteKey voteType, [FromQuery] string pubKeyOfMemberBeingVotedOn = "")
        {
            try
            {
                IEnumerable<Poll> polls = this.votingManager.GetExecutedPolls().Where(v => v.VotingData.Key == voteType);
                IEnumerable<PollViewModel> models = polls.Select(x => new PollViewModel(x, this.pollExecutor));

                if (!string.IsNullOrEmpty(pubKeyOfMemberBeingVotedOn))
                    models = models.Where(m => m.VotingDataString.Contains(pubKeyOfMemberBeingVotedOn));

                return this.Json(models);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves a list of whitelisted hashes.
        /// </summary>
        /// <returns>List of whitelisted hashes</returns>
        /// <response code="200">Returns the hashes</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("whitelistedhashes")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetWhitelistedHashes()
        {
            try
            {
                IEnumerable<HashModel> hashes = this.whitelistedHashesRepository.GetHashes().Select(x => new HashModel() { Hash = x.ToString() });

                return this.Json(hashes);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Votes to add a hash to the whitelist.
        /// </summary>
        /// <returns>The HTTP response</returns>
        /// <response code="200">Voted to add hash to whitelist</response>
        /// <response code="400">Invalid request, node is not a federation member, or an unexpected exception occurred</response>
        /// <response code="500">The request is null</response>
        [Route("schedulevote-whitelisthash")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult VoteWhitelistHash([FromBody]HashModel request)
        {
            return this.VoteWhitelistRemoveHashMember(request, true);
        }

        /// <summary>
        /// Votes to remove a hash from the whitelist.
        /// </summary>
        /// <returns>The HTTP response</returns>
        /// <response code="200">Voted to remove hash from whitelist</response>
        /// <response code="400">Invalid request, node is not a federation member, or an unexpected exception occurred</response>
        /// <response code="500">The request is null</response>
        [Route("schedulevote-removehash")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult VoteRemoveHash([FromBody]HashModel request)
        {
            return this.VoteWhitelistRemoveHashMember(request, false);
        }

        private IActionResult VoteWhitelistRemoveHashMember(HashModel request, bool whitelist)
        {
            Guard.NotNull(request, nameof(request));

            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            if (!this.federationManager.IsFederationMember)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Only federation members can vote", string.Empty);

            try
            {
                var hash = new uint256(request.Hash);

                this.votingManager.ScheduleVote(new VotingData()
                {
                    Key = whitelist ? VoteKey.WhitelistHash : VoteKey.RemoveHash,
                    Data = hash.ToBytes()
                });

                return this.Ok();
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "There was a problem executing a command.", e.ToString());
            }
        }

        /// <summary>
        /// Retrieves the scheduled voting data.
        /// </summary>
        /// <returns>Scheduled voting data</returns>
        /// <response code="200">Returns the voting data</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("scheduledvotes")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetScheduledVotes()
        {
            try
            {
                List<VotingData> votes = this.votingManager.GetScheduledVotes();

                IEnumerable<VotingDataModel> models = votes.Select(x => new VotingDataModel(x));

                return this.Json(models);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
