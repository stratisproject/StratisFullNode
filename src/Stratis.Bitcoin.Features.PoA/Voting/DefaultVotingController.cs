using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.PoA.Models;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class DefaultVotingController : Controller
    {
        protected readonly IFederationManager federationManager;

        private readonly IIdleFederationMembersKicker idleFederationMembersKicker;

        private readonly IInitialBlockDownloadState initialBlockDownloadState;

        protected readonly ILogger logger;

        protected readonly Network network;

        private readonly IPollResultExecutor pollExecutor;

        protected readonly VotingManager votingManager;

        private readonly IWhitelistedHashesRepository whitelistedHashesRepository;

        public DefaultVotingController(
            IFederationManager federationManager,
            ILoggerFactory loggerFactory,
            VotingManager votingManager,
            IWhitelistedHashesRepository whitelistedHashesRepository,
            Network network,
            IPollResultExecutor pollExecutor,
            IIdleFederationMembersKicker idleFederationMembersKicker,
            IInitialBlockDownloadState initialBlockDownloadState)
        {
            this.federationManager = federationManager;
            this.idleFederationMembersKicker = idleFederationMembersKicker;
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.network = network;
            this.pollExecutor = pollExecutor;
            this.votingManager = votingManager;
            this.whitelistedHashesRepository = whitelistedHashesRepository;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
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
                Dictionary<PubKey, uint> activeTimes = this.idleFederationMembersKicker.GetFederationMembersByLastActiveTime();
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
        public IActionResult GetPendingPolls()
        {
            try
            {
                List<Poll> polls = this.votingManager.GetPendingPolls();

                IEnumerable<PollViewModel> models = polls.Select(x => new PollViewModel(x, this.pollExecutor));

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
        public IActionResult GetFinishedPolls()
        {
            try
            {
                List<Poll> polls = this.votingManager.GetFinishedPolls();

                IEnumerable<PollViewModel> models = polls.Select(x => new PollViewModel(x, this.pollExecutor));

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
        public IActionResult GetExecutedPolls([FromQuery] string pubKey)
        {
            try
            {
                List<Poll> polls = this.votingManager.GetExecutedPolls();

                IEnumerable<PollViewModel> models = polls.Select(x => new PollViewModel(x, this.pollExecutor));

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

        /// <summary>
        /// Manually schedule any pending add federation member polls.
        /// </summary>
        /// <returns>Scheduled voting data</returns>
        /// <response code="200">Returns the voting data</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("schedule-pending-addmember-polls")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult ScheduleAddFederationMemberPolls()
        {
            if (this.initialBlockDownloadState.IsInitialBlockDownload())
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Votes can ony be scheduled once the node is out of IBD.", string.Empty);

            if (!this.federationManager.IsFederationMember)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Only federation members can schedule votes.", string.Empty);

            try
            {
                List<Poll> pendingAddFederationMemberPolls = this.votingManager.GetPendingPolls(VoteKey.AddFederationMember);

                //Filter all polls where this federation number has not voted on.
                pendingAddFederationMemberPolls = pendingAddFederationMemberPolls.Where(p => !p.PubKeysHexVotedInFavor.Contains(this.federationManager.CurrentFederationKey.PubKey.ToString())).ToList();

                if (!pendingAddFederationMemberPolls.Any())
                    return this.Ok("There are no pending add federation member polls to schedule for this federation member.");

                IFederationMember collateralFederationMember = this.federationManager.GetCurrentFederationMember();

                var poaConsensusFactory = this.network.Consensus.ConsensusFactory as PoAConsensusFactory;

                var result = new List<ScheduleAddFederationMemberPollResult>();
                foreach (Poll poll in pendingAddFederationMemberPolls)
                {
                    // If this member is already a federation member, skip.
                    PubKey memberVotedOnPubKey = poaConsensusFactory.DeserializeFederationMember(poll.VotingData.Data).PubKey;
                    if (this.federationManager.GetFederationMembers().Select(f => f.PubKey).Contains(memberVotedOnPubKey))
                        continue;

                    this.votingManager.ScheduleVote(new VotingData()
                    {
                        Key = VoteKey.AddFederationMember,
                        Data = poll.VotingData.Data
                    });

                    result.Add(new ScheduleAddFederationMemberPollResult() { PubKey = memberVotedOnPubKey });
                }

                return this.Ok(result);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "There was a problem executing a command.", e.ToString());
            }
        }
    }
}
