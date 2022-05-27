﻿using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;

namespace Stratis.Features.FederatedPeg.Controllers
{
    /// <summary>
    /// Vote on collateral federation members
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class CollateralVotingController : Controller
    {
        protected readonly IFederationManager fedManager;

        protected readonly VotingManager votingManager;

        protected readonly Network network;

        protected readonly ILogger logger;

        public CollateralVotingController(IFederationManager fedManager, VotingManager votingManager, Network network)
        {
            this.fedManager = fedManager;
            this.votingManager = votingManager;
            this.network = network;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>Schedules a vote to add or kick a federation member.</summary>
        /// <param name="request">See <see cref="CollateralFederationMemberModel"/>.</param>
        /// <response code="400">Invalid request</response>
        /// <response code="500">Request is null</response>
        /// <returns>HTTP response</returns>
        [Route("scheduleVote-kickFedMember")]
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), (int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult VoteKickFedMember([FromBody] CollateralFederationMemberModel request)
        {
            return this.VoteAddKickFedMember(request, false);
        }

        private IActionResult VoteAddKickFedMember(CollateralFederationMemberModel request, bool addMember)
        {
            Guard.NotNull(request, nameof(request));

            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            if (!this.fedManager.IsFederationMember)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Only federation members can vote", string.Empty);

            try
            {
                var key = new PubKey(request.PubKeyHex);

                if (this.fedManager.IsMultisigMember(key))
                    return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Multisig members can't be voted on", string.Empty);

                IFederationMember federationMember = new CollateralFederationMember(key, false, new Money(request.CollateralAmountSatoshis), request.CollateralMainchainAddress);

                byte[] fedMemberBytes = (this.network.Consensus.ConsensusFactory as PoAConsensusFactory).SerializeFederationMember(federationMember);

                this.votingManager.ScheduleVote(new VotingData()
                {
                    Key = addMember ? VoteKey.AddFederationMember : VoteKey.KickFederationMember,
                    Data = fedMemberBytes
                });

                return this.Ok();
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "There was a problem executing a command.", e.ToString());
            }
        }
    }
}
