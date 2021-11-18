using System;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using NLog;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Utilities.JsonErrors;

namespace Stratis.Features.ExternalApi.Controllers
{
    /// <summary>
    /// Controller for the External Api.
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class ExternalApiController : Controller
    {
        /// <summary>
        /// Name of api method which estimates the total gas a conversion will require.
        /// </summary>
        public const string EstimateConversionGasEndpoint = "estimateconversiongas";

        /// <summary>
        /// Name of api method which estimates the conversion fee (in STRAX).
        /// </summary>
        public const string EstimateConversionFeeEndpoint = "estimateconversionfee";

        /// <summary>
        /// Name of api method which estimates a recommended gas price based on historical measured samples.
        /// </summary>
        public const string GasPriceEndpoint = "gasprice";

        /// <summary>
        /// Name of api method which returns the most recently retrieved Stratis price.
        /// </summary>
        public const string StratisPriceEndpoint = "stratisprice";

        /// <summary>
        /// Name of api method which returns the most recently retrieved Ethereum price.
        /// </summary>
        public const string EthereumPriceEndpoint = "ethereumprice";

        private readonly IExternalApiPoller externalApiPoller;

        private readonly ILogger logger;

        /// <summary>
        /// The class constructor.
        /// </summary>
        /// <param name="externalApiPoller">The <see cref="IExternalApiPoller"/>.</param>
        public ExternalApiController(IExternalApiPoller externalApiPoller)
        {
            this.externalApiPoller = externalApiPoller;
            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Estimates the total gas a conversion will require.
        /// </summary>
        /// <returns>The total gas a conversion will require or an <see cref="ErrorResult"/>.</returns>
        [Route(EstimateConversionGasEndpoint)]
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

        /// <summary>
        /// Estimates the conversion fee (in STRAX).
        /// </summary>
        /// <returns>The conversion fee or and <see cref="ErrorResult"/>.</returns>
        [Route(EstimateConversionFeeEndpoint)]
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

        /// <summary>
        /// Estimates a recommended gas price based on historical samples.
        /// </summary>
        /// <returns>The recommended gas price or an <see cref="ErrorResult"/>.</returns>
        [Route(GasPriceEndpoint)]
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

        /// <summary>
        /// Returns the most recently retrieved Stratis price.
        /// </summary>
        /// <returns>The most recently retrieved Stratis price or an <see cref="ErrorResult"/>.</returns>
        [Route(StratisPriceEndpoint)]
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

        /// <summary>
        /// Returns the most recently retrieved Ethereum price.
        /// </summary>
        /// <returns>The most recently retrieved Ethereum price or an <see cref="ErrorResult"/>.</returns>
        [Route(EthereumPriceEndpoint)]
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
