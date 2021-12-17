using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;
using Stratis.Features.PoA.Voting;

namespace Stratis.Features.Collateral
{
    public static class CollateralRouteEndPoint
    {
        public const string JoinFederation = "joinfederation";
    }

    /// <summary>Controller providing operations on collateral federation members.</summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public sealed class CollateralController : Controller
    {
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IJoinFederationRequestService joinFederationRequestService;

        public CollateralController(
            IJoinFederationRequestService joinFederationRequestService,
            Network network)
        {
            this.joinFederationRequestService = joinFederationRequestService;
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
        }

        /// <summary>
        /// Called by a miner wanting to join the federation.
        /// </summary>
        /// <param name="request">See <see cref="JoinFederationRequestModel"></see>.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An instance of <see cref="JoinFederationResponseModel"/>.</returns>
        /// <response code="200">Returns a valid response.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(CollateralRouteEndPoint.JoinFederation)]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> JoinFederationAsync([FromBody] JoinFederationRequestModel request, CancellationToken cancellationToken = default)
        {
            Guard.NotNull(request, nameof(request));

            // Checks that the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.Trace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            if (!(this.network.Consensus.Options as PoAConsensusOptions).AutoKickIdleMembers)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", "This feature is currently disabled.");

            try
            {
                PubKey minerPubKey = await this.joinFederationRequestService.JoinFederationAsync(request, cancellationToken).ConfigureAwait(false);

                var model = new JoinFederationResponseModel
                {
                    MinerPublicKey = minerPubKey.ToHex()
                };

                this.logger.Trace("(-):'{0}'", model);
                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                this.logger.Trace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
