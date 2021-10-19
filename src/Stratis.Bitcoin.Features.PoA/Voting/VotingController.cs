using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Features.PoA.Models;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public sealed class VotingController : Controller
    {
        private readonly ChainIndexer chainIndexer;
        private readonly IFederationManager federationManager;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IPollResultExecutor pollExecutor;
        private readonly VotingManager votingManager;
        private readonly IWhitelistedHashesRepository whitelistedHashesRepository;

        public VotingController(
            ChainIndexer chainIndexer,
            IFederationManager federationManager,
            Network network,
            VotingManager votingManager,
            IWhitelistedHashesRepository whitelistedHashesRepository,
            IPollResultExecutor pollExecutor)
        {
            this.chainIndexer = chainIndexer;
            this.federationManager = federationManager;
            this.network = network;
            this.pollExecutor = pollExecutor;
            this.votingManager = votingManager;
            this.whitelistedHashesRepository = whitelistedHashesRepository;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Retrieves the tip of the polls repository.
        /// </summary>
        /// <returns>The poll repository tip.</returns>
        /// <response code="200">The request succeeded.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("polls/tip")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetPollsRepositoryTip()
        {
            try
            {
                var model = new PollsRepositoryHeightModel() { TipHeight = 0, TipHeightPercentage = 0 };
                ChainedHeader pollsRepoTip = this.votingManager.GetPollsRepositoryTip();
                if (this.chainIndexer.Tip != null && pollsRepoTip != null)
                {
                    model = new PollsRepositoryHeightModel()
                    {
                        TipHeight = pollsRepoTip.Height,
                        TipHeightPercentage = (int)((decimal)pollsRepoTip.Height / this.chainIndexer.Tip.Height * 100)
                    };
                }

                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves a list of pending or "active" polls.
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
                this.logger.Error("Exception occurred: {0}", e.ToString());
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
                this.logger.Error("Exception occurred: {0}", e.ToString());
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
                this.logger.Error("Exception occurred: {0}", e.ToString());
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
                this.logger.Error("Exception occurred: {0}", e.ToString());
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
        public IActionResult VoteWhitelistHash([FromBody] HashModel request)
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
        public IActionResult VoteRemoveHash([FromBody] HashModel request)
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
                this.logger.Error("Exception occurred: {0}", e.ToString());
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
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Votes to kick/remove a member from the federation.
        /// </summary>
        /// <returns>The HTTP response</returns>
        /// <response code="200">Voted to remove a member from the federation.</response>
        /// <response code="400">Invalid request, node is not a federation member, or an unexpected exception occurred</response>
        /// <response code="500">The request is null</response>
        [Route("schedulevote-kickmember")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult VoteKickFederationMember([FromBody] KickFederationMemberModel model)
        {
            Guard.NotNull(model, nameof(model));

            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            if (!this.federationManager.IsFederationMember)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Only federation members can vote.", string.Empty);

            try
            {
                IFederationMember federationMember = this.federationManager.GetFederationMembers().SingleOrDefault(m => m.PubKey.ToHex() == model.PubKey);
                if (federationMember == null)
                    return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, $"'{model.PubKey}' is not currently a federation member.", string.Empty);

                var consensusFactory = this.network.Consensus.ConsensusFactory as PoAConsensusFactory;
                byte[] federationMemberBytes = consensusFactory.SerializeFederationMember(federationMember);

                bool alreadyKicking = this.votingManager.AlreadyVotingFor(VoteKey.KickFederationMember, federationMemberBytes);
                if (alreadyKicking)
                    return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, $"Skipping because kicking {model.PubKey} is already being voted on.", string.Empty);

                this.votingManager.ScheduleVote(new VotingData()
                {
                    Key = VoteKey.KickFederationMember,
                    Data = federationMemberBytes
                });

                return Ok($"A vote to kick '{model.PubKey}' has now been scheduled.");
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "There was a problem executing a vote to be scheduled.", e.ToString());
            }
        }
    }
}
