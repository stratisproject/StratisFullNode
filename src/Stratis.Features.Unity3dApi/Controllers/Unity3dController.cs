using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.BlockStore.Controllers;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;

namespace Stratis.Features.Unity3dApi.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class Unity3dController : Controller
    {
        private readonly NodeController nodeController;

        private readonly IAddressIndexer addressIndexer;

        private readonly IUtxoIndexer utxoIndexer;

        /// <summary>Provides access to the block store on disk.</summary>
        private readonly IBlockStore blockStore;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>An interface that provides information about the chain and validation.</summary>
        private readonly IChainState chainState;

        /// <summary>The chain.</summary>
        private readonly ChainIndexer chainIndexer;

        /// <summary>Current network for the active controller instance.</summary>
        private readonly Network network;
        
        private readonly IStakeChain stakeChain;

        public Unity3dController(Network network,
            ILoggerFactory loggerFactory,
            IBlockStore blockStore,
            IChainState chainState,
            ChainIndexer chainIndexer,
            IAddressIndexer addressIndexer,
            IUtxoIndexer utxoIndexer, 
            NodeController nodeController,
            IStakeChain stakeChain = null)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.nodeController = nodeController;
            this.addressIndexer = addressIndexer;
            this.network = network;
            this.blockStore = blockStore;
            this.chainState = chainState;
            this.chainIndexer = chainIndexer;
            this.utxoIndexer = utxoIndexer;
            this.stakeChain = stakeChain;
        }
        
        [HttpGet]
        public IActionResult Get()
        {
            return this.Json("API works");
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
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            try
            {
                uint256 blockId = uint256.Parse(query.Hash);

                ChainedHeader chainedHeader = this.chainIndexer.GetHeader(blockId);

                if (chainedHeader == null)
                    return this.Ok("Block not found");

                Block block = chainedHeader.Block ?? this.blockStore.GetBlock(blockId);

                // In rare occasions a block that is found in the
                // indexer may not have been pushed to the store yet. 
                if (block == null)
                    return this.Ok("Block not found");

                if (!query.OutputJson)
                {
                    return this.Json(block);
                }

                BlockModel blockModel = query.ShowTransactionDetails
                    ? new BlockTransactionDetailsModel(block, chainedHeader, this.chainIndexer.Tip, this.network)
                    : new BlockModel(block, chainedHeader, this.chainIndexer.Tip, this.network);

                if (this.network.Consensus.IsProofOfStake)
                {
                    var posBlock = block as PosBlock;

                    blockModel.PosBlockSignature = posBlock.BlockSignature.ToHex(this.network);
                    blockModel.PosBlockTrust = new Target(chainedHeader.GetBlockTarget()).ToUInt256().ToString();
                    blockModel.PosChainTrust = chainedHeader.ChainWork.ToString(); // this should be similar to ChainWork

                    if (this.stakeChain != null)
                    {
                        BlockStake blockStake = this.stakeChain.Get(blockId);

                        blockModel.PosModifierv2 = blockStake?.StakeModifierV2.ToString();
                        blockModel.PosFlags = blockStake?.Flags == BlockFlag.BLOCK_PROOF_OF_STAKE ? "proof-of-stake" : "proof-of-work";
                        blockModel.PosHashProof = blockStake?.HashProof?.ToString();
                    }
                }

                return this.Json(blockModel);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
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
            try
            {
                ChainedHeader addressIndexerTip = this.addressIndexer.IndexerTip;
                return this.Json(new AddressIndexerTipModel() { TipHash = addressIndexerTip?.HashBlock, TipHeight = addressIndexerTip?.Height });
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
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
            try
            {
                string[] addressesArray = addresses.Split(',');

                this.logger.LogDebug("Asking data for {0} addresses.", addressesArray.Length);

                AddressBalancesResult result = this.addressIndexer.GetAddressBalances(addressesArray, minConfirmations);

                this.logger.LogDebug("Sending data for {0} addresses.", result.Balances.Count);

                return this.Json(result);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
