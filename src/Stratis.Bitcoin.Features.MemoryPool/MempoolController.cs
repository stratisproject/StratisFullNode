using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Controllers;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;

namespace Stratis.Bitcoin.Features.MemoryPool
{
    /// <summary>
    /// Retrieve information about unconfirmed transactions
    /// </summary>
    [ApiVersion("1")]
    public class MempoolController : FeatureController
    {
        public MempoolManager MempoolManager { get; private set; }
        private readonly ILogger logger;

        public MempoolController(ILoggerFactory loggerFactory, MempoolManager mempoolManager)
        {
            Guard.NotNull(mempoolManager, nameof(mempoolManager));

            this.MempoolManager = mempoolManager;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        [ActionName("getrawmempool")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [ActionDescription("Lists the contents of the memory pool.")]
        public Task<List<uint256>> GetRawMempool()
        {
            return this.MempoolManager.GetMempoolAsync();
        }

        /// <summary>
        /// Gets a hash of each transaction in the memory pool. In other words, a list of the TX IDs for all the transactions in the mempool are retrieved.
        /// </summary>
        /// <returns>Json formatted <see cref="List{uint256}"/> containing the memory pool contents. Returns <see cref="IActionResult"/> formatted error if fails.</returns>
        /// <response code="200">Returns memory pool transaction hashes</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("api/[controller]/getRawMempool")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType(typeof(ErrorResponse), (int)HttpStatusCode.BadRequest)]
        public async Task<IActionResult> GetRawMempoolAsync()
        {
            try
            {
                return this.Json(await this.GetRawMempool().ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
