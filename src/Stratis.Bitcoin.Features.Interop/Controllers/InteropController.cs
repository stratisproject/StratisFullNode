using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Models;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Coordination;

namespace Stratis.Bitcoin.Features.Interop.Controllers
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public sealed class InteropController : Controller
    {
        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IETHCompatibleClientProvider ethCompatibleClientProvider;
        private readonly IFederationManager federationManager;
        private readonly InteropSettings interopSettings;
        private readonly ILogger logger;
        private readonly Network network;

        public InteropController(
            Network network,
            IConversionRequestCoordinationService conversionRequestCoordinationService,
            IConversionRequestRepository conversionRequestRepository,
            IETHCompatibleClientProvider ethCompatibleClientProvider,
            IFederationManager federationManager,
            InteropSettings interopSettings)
        {
            this.conversionRequestCoordinationService = conversionRequestCoordinationService;
            this.conversionRequestRepository = conversionRequestRepository;
            this.ethCompatibleClientProvider = ethCompatibleClientProvider;
            this.federationManager = federationManager;
            this.interopSettings = interopSettings;
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
        }

        [Route("status")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult InteropStatus()
        {
            try
            {
                var response = new InteropStatusResponseModel();

                var mintRequests = new List<ConversionRequestModel>();

                foreach (ConversionRequest request in this.conversionRequestRepository.GetAllMint(false))
                {
                    mintRequests.Add(new ConversionRequestModel()
                    {
                        RequestId = request.RequestId,
                        RequestType = request.RequestType,
                        RequestStatus = request.RequestStatus,
                        BlockHeight = request.BlockHeight,
                        DestinationAddress = request.DestinationAddress,
                        DestinationChain = request.DestinationChain.ToString(),
                        Amount = request.Amount,
                        Processed = request.Processed,
                        Status = request.RequestStatus.ToString(),
                    });
                }

                response.MintRequests = mintRequests.OrderByDescending(m => m.BlockHeight).ToList();

                var burnRequests = new List<ConversionRequestModel>();

                foreach (ConversionRequest request in this.conversionRequestRepository.GetAllBurn(false))
                {
                    burnRequests.Add(new ConversionRequestModel()
                    {
                        RequestId = request.RequestId,
                        RequestType = request.RequestType,
                        RequestStatus = request.RequestStatus,
                        BlockHeight = request.BlockHeight,
                        DestinationAddress = request.DestinationAddress,
                        DestinationChain = request.DestinationChain.ToString(),
                        Amount = request.Amount,
                        Processed = request.Processed,
                        Status = request.RequestStatus.ToString(),
                    });
                }

                response.BurnRequests = burnRequests.OrderByDescending(m => m.BlockHeight).ToList();

                var receivedVotes = new Dictionary<string, List<string>>();

                foreach ((string requestId, HashSet<PubKey> pubKeys) in this.conversionRequestCoordinationService.GetStatus())
                {
                    var pubKeyList = new List<string>();

                    foreach (PubKey pubKey in pubKeys)
                    {
                        pubKeyList.Add(pubKey.ToHex());
                    }

                    receivedVotes.Add(requestId, pubKeyList);
                }

                response.ReceivedVotes = receivedVotes;

                return this.Json(response);
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves the list of current owners for the multisig wallet contract.
        /// </summary>
        /// <param name="destinationChain">The chain the multisig wallet contract is deployed to.</param>
        /// <returns>The list of owner accounts for the multisig wallet contract.</returns>
        [Route("owners")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> OwnersAsync(DestinationChain destinationChain)
        {
            try
            {
                if (!this.ethCompatibleClientProvider.IsChainSupportedAndEnabled(destinationChain))
                    return this.Json($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                return this.Json(await client.GetOwnersAsync().ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Creates and broadcasts an 'addOwner()' contract call on the multisig wallet contract.
        /// This can only be done by one of the current owners of the contract, and needs to be confirmed by a sufficient number of the other owners.
        /// </summary>
        /// <param name="destinationChain">The chain the multisig wallet contract is deployed to.</param>
        /// <param name="newOwnerAddress">The account of the new owner to be added.</param>
        /// <param name="gasPrice">The gas price to use for transaction submission.</param>
        /// <returns>The transactionId of the multisig wallet contract transaction, which is then used to confirm the transaction.</returns>
        [Route("addowner")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> AddOwnerAsync(DestinationChain destinationChain, string newOwnerAddress, int gasPrice)
        {
            try
            {
                if (!this.ethCompatibleClientProvider.IsChainSupportedAndEnabled(destinationChain))
                    return this.Json($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);
                string data = client.EncodeAddOwnerParams(newOwnerAddress);

                ETHInteropSettings settings = this.interopSettings.GetSettingsByChain(destinationChain);

                // TODO: Maybe for convenience the gas price could come from the external API poller
                return this.Json(await client.SubmitTransactionAsync(settings.MultisigWalletAddress, 0, data, gasPrice).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Creates and broadcasts a 'removeOwner()' contract call on the multisig wallet contract.
        /// This can only be done by one of the current owners of the contract, and needs to be confirmed by a sufficient number of the other owners.
        /// </summary>
        /// <param name="destinationChain">The chain the multisig wallet contract is deployed to.</param>
        /// <param name="existingOwnerAddress">The account of the owner to be removed.</param>
        /// <param name="gasPrice">The gas price to use for transaction submission.</param>
        /// <returns>The transactionId of the multisig wallet contract transaction, which is then used to confirm the transaction.</returns>
        [Route("removeowner")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> RemoveOwnerAsync(DestinationChain destinationChain, string existingOwnerAddress, int gasPrice)
        {
            try
            {
                if (!this.ethCompatibleClientProvider.IsChainSupportedAndEnabled(destinationChain))
                    return this.Json($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);
                string data = client.EncodeRemoveOwnerParams(existingOwnerAddress);

                ETHInteropSettings settings = this.interopSettings.GetSettingsByChain(destinationChain);

                // TODO: Maybe for convenience the gas price could come from the external API poller
                return this.Json(await client.SubmitTransactionAsync(settings.MultisigWalletAddress, 0, data, gasPrice).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Explicitly confirms a given multisig wallet contract transactionId by submitting a contract call transaction to the network.
        /// <remarks>This can only be called once per multisig owner. Additional calls by the same owner account will simply fail and waste gas.</remarks>
        /// </summary>
        /// <param name="destinationChain">The chain the multisig wallet contract is deployed to.</param>
        /// <param name="transactionId">The multisig wallet transactionId (this is an integer, not an on-chain transaction hash).</param>
        /// <param name="gasPrice">The gas price to use for submitting the confirmation.</param>
        /// <returns>The on-chain transaction hash of the contract call transaction.</returns>
        [Route("confirmtransaction")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> ConfirmTransactionAsync(DestinationChain destinationChain, int transactionId, int gasPrice)
        {
            try
            {
                if (!this.ethCompatibleClientProvider.IsChainSupportedAndEnabled(destinationChain))
                    return this.Json($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                return this.Json(await client.ConfirmTransactionAsync(transactionId, gasPrice).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Creates and broadcasts a 'changeRequirement()' contract call on the multisig wallet contract.
        /// This can only be done by one of the current owners of the contract, and needs to be confirmed by a sufficient number of the other owners.
        /// <remarks>This should only be done once all owner modifications are complete to save gas and orchestrating confirmations.</remarks>
        /// </summary>
        /// <param name="destinationChain">The chain the multisig wallet contract is deployed to.</param>
        /// <param name="requirement">The new threshold for confirmations on the multisig wallet contract. Can usually be numOwners / 2 rounded up.</param>
        /// <param name="gasPrice">The gas price to use for submitting the contract call transaction.</param>
        /// <returns>The on-chain transaction hash of the contract call transaction.</returns>
        [Route("changerequirement")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> ChangeRequirementAsync(DestinationChain destinationChain, int requirement, int gasPrice)
        {
            try
            {
                if (!this.ethCompatibleClientProvider.IsChainSupportedAndEnabled(destinationChain))
                    return this.Json($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                string data = client.EncodeChangeRequirementParams(requirement);

                ETHInteropSettings settings = this.interopSettings.GetSettingsByChain(destinationChain);

                // TODO: Maybe for convenience the gas price could come from the external API poller
                return this.Json(await client.SubmitTransactionAsync(settings.MultisigWalletAddress, 0, data, gasPrice).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.Error("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("requests/delete")]
        [HttpDelete]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult DeleteConversionRequests()
        {
            if (this.network.IsTest() || this.network.IsRegTest())
            {
                var result = this.conversionRequestRepository.DeleteConversionRequests();
                return this.Json($"{result} conversion requests have been deleted.");
            }

            return this.Json($"Deleting conversion requests is only available on test networks.");
        }

        /// <summary>
        /// Endpoint that allows the multisig operator to set itself as the originator (submittor) for a given request id.
        /// </summary>
        /// <param name="requestId">The request id in question.</param>
        [Route("requests/setoriginator")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult SetOriginatorForRequest([FromBody] string requestId)
        {
            try
            {
                this.conversionRequestRepository.SetConversionRequestState(requestId, ConversionRequestStatus.OriginatorNotSubmitted);
                return this.Json($"Conversion request '{requestId}' has been reset to {ConversionRequestStatus.OriginatorNotSubmitted}.");
            }
            catch (Exception e)
            {
                this.logger.Error("Exception setting conversion request '{0}' to {1} : {2}.", requestId, e.ToString(), ConversionRequestStatus.OriginatorNotSubmitted);

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", e.Message);
            }
        }

        /// <summary>
        /// Endpoint that allows the multisig operator to reset the request as NotOriginator.
        /// </summary>
        /// <param name="requestId">The request id in question.</param>
        [Route("requests/setnotoriginator")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult ResetConversionRequestAsNotOriginator([FromBody] string requestId)
        {
            try
            {
                this.conversionRequestRepository.SetConversionRequestState(requestId, ConversionRequestStatus.NotOriginator);
                return this.Json($"Conversion request '{requestId}' has been reset to {ConversionRequestStatus.NotOriginator}.");
            }
            catch (Exception e)
            {
                this.logger.Error("Exception setting conversion request '{0}' to {1} : {2}.", requestId, e.ToString(), ConversionRequestStatus.NotOriginator);

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", e.Message);
            }
        }

        /// <summary>
        /// Endpoint that allows the multisig operator to manually add a vote if they are originator of the request.
        /// </summary>
        /// <param name="model">The request id and vote in question.</param>
        [Route("requests/pushvote")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult PushVoteManually([FromBody] PushManualVoteForRequest model)
        {
            try
            {
                this.conversionRequestCoordinationService.AddVote(model.RequestId, BigInteger.Parse(model.EventId), this.federationManager.CurrentFederationKey.PubKey);
                this.conversionRequestRepository.SetConversionRequestState(model.RequestId, ConversionRequestStatus.OriginatorSubmitted);
                return this.Json($"Manual vote pushed for request '{model.RequestId}' with event id '{model.EventId}'.");
            }
            catch (Exception e)
            {
                this.logger.Error("Exception manual pushing vote for conversion request '{0}' : {1}.", model.RequestId, e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", e.Message);
            }
        }
    }
}
