using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.BlockStore.Controllers;
using Stratis.Bitcoin.Features.BlockStore.Models;

namespace Stratis.Features.Unity3dApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class Unity3dController : Controller
    {
        private readonly NodeController nodeController;

        private readonly BlockStoreController blockStoreController;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        public Unity3dController(ILoggerFactory loggerFactory, BlockStoreController blockStoreController, NodeController nodeController)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.nodeController = nodeController;
            this.blockStoreController = blockStoreController;
        }

        /// <summary>
        /// Gets the block header of a block identified by a block hash.
        /// </summary>
        /// <param name="hash">The hash of the block to retrieve.</param>
        /// <param name="isJsonFormat">A flag that specifies whether to return the block header in the JSON format. Defaults to true. A value of false is currently not supported.</param>
        /// <returns>Json formatted <see cref="BlockHeaderModel"/>. <c>null</c> if block not found. Returns <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/> formatted error if fails.</returns>
        /// <exception cref="NotImplementedException">Thrown if isJsonFormat = false</exception>"
        /// <exception cref="ArgumentException">Thrown if hash is empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if logger is not provided.</exception>
        /// <remarks>Binary serialization is not supported with this method.</remarks>
        [Route("getblockheader")]
        [HttpGet]
        public IActionResult GetBlockHeader([FromQuery] string hash, bool isJsonFormat = true)
        {
            return this.nodeController.GetBlockHeader(hash, isJsonFormat);
        }

        /// <summary>
        /// Gets a raw transaction that is present on this full node.
        /// This method first searches the transaction pool and then tries the block store.
        /// </summary>
        /// <param name="trxid">The transaction ID (a hash of the trancaction).</param>
        /// <param name="verbose">A flag that specifies whether to return verbose information about the transaction.</param>
        /// <returns>Json formatted <see cref="TransactionBriefModel"/> or <see cref="TransactionVerboseModel"/>. <c>null</c> if transaction not found. Returns <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/> formatted error if otherwise fails.</returns>
        /// <exception cref="ArgumentNullException">Thrown if fullNode, network, or chain are not available.</exception>
        /// <exception cref="ArgumentException">Thrown if trxid is empty or not a valid<see cref="uint256"/>.</exception>
        /// <remarks>Requires txindex=1, otherwise only txes that spend or create UTXOs for a wallet can be returned.</remarks>
        [Route("getrawtransaction")]
        [HttpGet]
        public async Task<IActionResult> GetRawTransactionAsync([FromQuery] string trxid, bool verbose = false)
        {
            return await this.nodeController.GetRawTransactionAsync(trxid, verbose);
        }

        /// <summary>
        /// Gets a JSON representation for a given transaction in hex format.
        /// </summary>
        /// <param name="request">A class containing the necessary parameters for a block search request.</param>
        /// <returns>The JSON representation of the transaction.</returns>
        [HttpPost]
        [Route("decoderawtransaction")]
        public IActionResult DecodeRawTransaction([FromBody] DecodeRawTransactionModel request)
        {
            return this.nodeController.DecodeRawTransaction(request);
        }

        /// <summary>
        /// Validates a bech32 or base58 bitcoin address.
        /// </summary>
        /// <param name="address">A Bitcoin address to validate in a string format.</param>
        /// <returns>Json formatted <see cref="ValidatedAddress"/> containing a boolean indicating address validity. Returns <see cref="Microsoft.AspNetCore.Mvc.IActionResult"/> formatted error if fails.</returns>
        /// <exception cref="ArgumentException">Thrown if address provided is empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if network is not provided.</exception>
        [Route("validateaddress")]
        [HttpGet]
        public IActionResult ValidateAddress([FromQuery] string address)
        {
            return this.nodeController.ValidateAddress(address);
        }


        /// <summary>
        /// Retrieves the block which matches the supplied block hash.
        /// </summary>
        /// <param name="query">An object containing the necessary parameters to search for a block.</param>
        /// <returns><see cref="BlockModel"/> if block is found, <see cref="NotFoundObjectResult"/> if not found. Returns <see cref="IActionResult"/> with error information if exception thrown.</returns>
        /// <response code="200">Returns data about the block or block not found message</response>
        /// <response code="400">Block hash invalid, or an unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetBlock)]
        [HttpGet]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        public IActionResult GetBlock([FromQuery] SearchByHashRequest query)
        {
            return this.blockStoreController.GetBlock(query);
        }

        /// <summary>
        /// Retrieves the <see cref="addressIndexer"/>'s tip. 
        /// </summary>
        /// <returns>An instance of <see cref="AddressIndexerTipModel"/> containing the tip's hash and height.</returns>
        /// <response code="200">Returns the address indexer tip</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("tip")]
        [HttpGet]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        public IActionResult GetTip()
        {
            return this.blockStoreController.GetAddressIndexerTip();
        }

        /// <summary>Provides balance of the given addresses confirmed with at least <paramref name="minConfirmations"/> confirmations.</summary>
        /// <param name="addresses">A comma delimited set of addresses that will be queried.</param>
        /// <param name="minConfirmations">Only blocks below consensus tip less this parameter will be considered.</param>
        /// <returns>A result object containing the balance for each requested address and if so, a message stating why the indexer is not queryable.</returns>
        /// <response code="200">Returns balances for the requested addresses</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetAddressesBalances)]
        [HttpGet]
        [ProducesResponseType((int) HttpStatusCode.OK)]
        [ProducesResponseType((int) HttpStatusCode.BadRequest)]
        public IActionResult GetAddressesBalances(string addresses, int minConfirmations)
        {
            return this.blockStoreController.GetAddressesBalances(addresses, minConfirmations);
        }
    }
}
