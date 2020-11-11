using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;

namespace Stratis.Features.Collateral
{
    public static class CollateralRouteEndPoint
    {
        public const string JoinFederation = "joinfederation";
    }

    /// <summary>Controller providing operations on collateral federation members.</summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class CollateralController : Controller
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Current network for the active controller instance.</summary>
        private readonly Network network;

        /// <summary>The collateral federation mananager.</summary>
        private readonly IFederationManager federationManager;

        public CollateralController(Network network,
            ILoggerFactory loggerFactory,
            IFederationManager federationManager)
        {
            this.network = network;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.federationManager = federationManager;
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
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            if (!(this.network.Consensus.Options as PoAConsensusOptions).AutoKickIdleMembers)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", "This feature is currently disabled.");

            try
            {
                PubKey minerPubKey = await (this.federationManager as CollateralFederationManager).JoinFederationAsync(request, cancellationToken);

                var model = new JoinFederationResponseModel
                {
                    MinerPublicKey = minerPubKey.ToHex()
                };

                this.logger.LogTrace("(-):'{0}'", model);
                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
