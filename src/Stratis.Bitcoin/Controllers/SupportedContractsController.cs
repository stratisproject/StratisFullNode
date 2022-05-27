﻿using System;
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
    /// <summary>
    /// Retrieve InterFlux token information
    /// </summary>
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
        /// <response type="200">Official InterFlux token details returned</response>
        /// <response type="400">Unexpected error occurred</response>
        [Route("list")]
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<SupportedContractAddress>), (int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorResponse), (int)HttpStatusCode.BadRequest)]
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