using System;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using NLog;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Utilities.JsonErrors;

namespace Stratis.Features.ExternalApi.Controllers
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class ExternalApiController : Controller
    {
        private readonly IExternalApiPoller externalApiPoller;

        private readonly ILogger logger;

        public ExternalApiController(IExternalApiPoller externalApiPoller)
        {
            this.externalApiPoller = externalApiPoller;
            this.logger = LogManager.GetCurrentClassLogger();
        }

        [Route("estimateconversiongas")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult EstimateConversionGas()
        {
            try
            {
                return this.Json(this.externalApiPoller.EstimateConversionTransactionGas());
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("estimateconversionfee")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult EstimateConversionFee()
        {
            try
            {
                return this.Json(this.externalApiPoller.EstimateConversionTransactionFee());
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("gasprice")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GasPrice()
        {
            try
            {
                return this.Json(this.externalApiPoller.GetGasPrice());
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("stratisprice")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult StratisPrice()
        {
            try
            {
                return this.Json(this.externalApiPoller.GetStratisPrice());
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("ethereumprice")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult EthereumPrice()
        {
            try
            {
                return this.Json(this.externalApiPoller.GetEthereumPrice());
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
