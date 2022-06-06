using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Interop.Contracts;

namespace Stratis.Bitcoin.Controllers
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public sealed class SupportedContractsController : Controller
    {
        private readonly ILogger logger;

        public SupportedContractsController()
        {
            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Endpoint that returns the supported/official native chain/SRC20 token contract addresses for the given network type.
        /// </summary>
        /// <param name="networkType">The network type to return the addresses for.</param>
        [Route("list")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult ListSupportedContractAddresses(NetworkType networkType)
        {
            try
            {
                List<SupportedContractAddress> result = SupportedContractAddresses.ForNetwork(networkType);
                return this.Json(result);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred trying to retrieve the supported contract addresses for '{0}' : {1}.", networkType, e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", e.Message);
            }
        }
    }
}