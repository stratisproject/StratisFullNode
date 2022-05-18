using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.Interop.ETHClient;
using Stratis.Bitcoin.Features.Interop.Models;
using Stratis.Bitcoin.Features.Interop.Settings;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Coordination;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Serialization;

namespace Stratis.Bitcoin.Features.Interop.Controllers
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public sealed class InteropController : Controller
    {
        private readonly ICallDataSerializer callDataSerializer;
        private readonly ChainIndexer chainIndexer;
        private readonly IContractPrimitiveSerializer contractPrimitiveSerializer;
        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IConversionRequestFeeKeyValueStore conversionRequestFeeKeyValueStore;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IETHCompatibleClientProvider ethCompatibleClientProvider;
        private readonly IFederationManager federationManager;
        private readonly InteropSettings interopSettings;
        private readonly InteropPoller interopPoller;
        private readonly ILogger logger;
        private readonly IReplenishmentKeyValueStore replenishmentKeyValueStore;
        private readonly Network network;

        public InteropController(
            ICallDataSerializer callDataSerializer,
            ChainIndexer chainIndexer,
            IContractPrimitiveSerializer contractPrimitiveSerializer,
            Network network,
            IConversionRequestCoordinationService conversionRequestCoordinationService,
            IConversionRequestFeeKeyValueStore conversionRequestFeeKeyValueStore,
            IConversionRequestRepository conversionRequestRepository,
            IETHCompatibleClientProvider ethCompatibleClientProvider,
            IFederationManager federationManager,
            InteropSettings interopSettings,
            InteropPoller interopPoller,
            IReplenishmentKeyValueStore replenishmentKeyValueStore)
        {
            this.callDataSerializer = callDataSerializer;
            this.chainIndexer = chainIndexer;
            this.contractPrimitiveSerializer = contractPrimitiveSerializer;
            this.conversionRequestCoordinationService = conversionRequestCoordinationService;
            this.conversionRequestFeeKeyValueStore = conversionRequestFeeKeyValueStore;
            this.conversionRequestRepository = conversionRequestRepository;
            this.ethCompatibleClientProvider = ethCompatibleClientProvider;
            this.federationManager = federationManager;
            this.interopSettings = interopSettings;
            this.interopPoller = interopPoller;
            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.replenishmentKeyValueStore = replenishmentKeyValueStore;
        }

        [Route("initializeinterflux")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult InitializeInterflux([FromBody] InitializeInterfluxRequestModel model)
        {
            try
            {
                this.interopSettings.GetSettings<CirrusInteropSettings>().CirrusWalletCredentials = new WalletCredentials()
                {
                    WalletName = model.WalletName,
                    WalletPassword = model.WalletPassword,
                    AccountName = model.AccountName
                };

                this.logger.LogInformation("Interop wallet credentials set.");

                return this.Json(true);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("configuration")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult Configuration(DestinationChain destinationChain)
        {
            try
            {
                return Ok(this.interopSettings.GetSettingsByChain(destinationChain));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("state")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult InteropState()
        {
            try
            {
                List<ConversionRequest> burns = this.conversionRequestRepository.GetAllBurn(false);
                List<ConversionRequest> mints = this.conversionRequestRepository.GetAllMint(false);
                var burnsCount = burns.Count;
                var mintsCount = mints.Count;

                return this.Json(new
                {
                    burnsCount = burns.Count,
                    mintsCount = mints.Count,
                    burnsUnprocessed = burns.Count(b => !b.Processed),
                    mintsUnprocessed = mints.Count(b => !b.Processed),
                });
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("request")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult InteropStatusBurnRequests([FromBody] string requestId)
        {
            try
            {
                ConversionRequest request = this.conversionRequestRepository.Get(requestId);
                if (request == null)
                    return BadRequest($"'{requestId}' does not exist.");

                return this.Json(ConstructConversionRequestModel(request));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("burns")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult InteropStatusBurnRequests()
        {
            try
            {
                return this.Json(this.conversionRequestRepository.GetAllBurn(false).Select(request => ConstructConversionRequestModel(request)).OrderByDescending(m => m.BlockHeight));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("mints")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult InteropStatusMintRequests()
        {
            try
            {
                return this.Json(this.conversionRequestRepository.GetAllMint(false).Select(request => ConstructConversionRequestModel(request)).OrderByDescending(m => m.BlockHeight));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("replenishments")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult InteropReplenishments()
        {
            try
            {
                return this.Json(this.replenishmentKeyValueStore.GetAllAsJson<ReplenishmentTransaction>());
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("request/delete")]
        [HttpDelete]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult DeleteConversionRequest(string requestId)
        {
            try
            {
                // Delete the conversion request.
                this.conversionRequestRepository.DeleteConversionRequest(requestId);

                // Delete any associated fees.
                this.conversionRequestFeeKeyValueStore.Delete(requestId);

                return Ok($"{requestId} has been deleted.");
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("status/votes")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult InteropStatusVotes()
        {
            try
            {
                var response = new InteropStatusResponseModel();

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
                this.logger.LogError("Exception occurred: {0}", e.ToString());
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
                    return BadRequest($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                return this.Json(await client.GetOwnersAsync().ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

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
                    return BadRequest($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);
                string data = client.EncodeAddOwnerParams(newOwnerAddress);

                ETHInteropSettings settings = this.interopSettings.GetSettingsByChain(destinationChain);

                // TODO: Maybe for convenience the gas price could come from the external API poller
                return this.Json(await client.SubmitTransactionAsync(settings.MultisigWalletAddress, 0, data, gasPrice).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

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
                    return BadRequest($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);
                string data = client.EncodeRemoveOwnerParams(existingOwnerAddress);

                ETHInteropSettings settings = this.interopSettings.GetSettingsByChain(destinationChain);

                // TODO: Maybe for convenience the gas price could come from the external API poller
                return this.Json(await client.SubmitTransactionAsync(settings.MultisigWalletAddress, 0, data, gasPrice).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

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
                    return BadRequest($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                // TODO: Maybe for convenience the gas price could come from the external API poller
                return this.Json(await client.ConfirmTransactionAsync(transactionId, gasPrice).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

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
        /// <returns>The multisig wallet transactionId of the changerequirement call.</returns>
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
                    return BadRequest($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                string data = client.EncodeChangeRequirementParams(requirement);

                ETHInteropSettings settings = this.interopSettings.GetSettingsByChain(destinationChain);

                // TODO: Maybe for convenience the gas price could come from the external API poller
                return this.Json(await client.SubmitTransactionAsync(settings.MultisigWalletAddress, 0, data, gasPrice).ConfigureAwait(false));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves a multisig wallet transaction.
        /// </summary>
        /// <param name="destinationChain">The chain the multisig wallet contract is deployed to.</param>
        /// <param name="transactionId">The multisig wallet transactionId (this is an integer, not an on-chain transaction hash).</param>
        /// <param name="raw">Indicates whether to partially decode the transaction or leave it in raw hex format.</param>
        /// <returns>The multisig wallet transaction data.</returns>
        [Route("multisigtransaction")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> MultisigTransactionAsync(DestinationChain destinationChain, int transactionId, bool raw)
        {
            try
            {
                if (!this.ethCompatibleClientProvider.IsChainSupportedAndEnabled(destinationChain))
                    return BadRequest($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                if (raw)
                    return this.Json(await client.GetRawMultisigTransactionAsync(transactionId).ConfigureAwait(false));

                TransactionDTO transaction = await client.GetMultisigTransactionAsync(transactionId).ConfigureAwait(false);

                var response = new TransactionResponseModel()
                {
                    Destination = transaction.Destination,
                    Value = transaction.Value.ToString(),
                    Data = Encoders.Hex.EncodeData(transaction.Data),
                    Executed = transaction.Executed
                };

                return this.Json(response);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Returns the list of contract owners that confirmed a particular multisig transaction.
        /// </summary>
        /// <param name="destinationChain">The chain the multisig wallet contract is deployed to.</param>
        /// <param name="transactionId">The multisig wallet transactionId (this is an integer, not an on-chain transaction hash).</param>
        /// <returns>A list of owner addresses that confirmed the transaction.</returns>
        [Route("multisigconfirmations")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> MultisigConfirmationsAsync(DestinationChain destinationChain, int transactionId)
        {
            try
            {
                if (!this.ethCompatibleClientProvider.IsChainSupportedAndEnabled(destinationChain))
                    return BadRequest($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                List<string> owners = await client.GetOwnersAsync().ConfigureAwait(false);

                var ownersConfirmed = new List<string>();

                foreach (string multisig in owners)
                {
                    bool confirmed = await client.AddressConfirmedTransactionAsync(transactionId, multisig).ConfigureAwait(false);

                    if (confirmed)
                        ownersConfirmed.Add(multisig);
                }

                return this.Json(ownersConfirmed);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves the wSTRAX balance of a given account.
        /// </summary>
        /// <param name="destinationChain">The chain the wSTRAX ERC20 contract is deployed to.</param>
        /// <param name="account">The account to retrieve the balance for.</param>
        /// <returns>The account balance.</returns>
        [Route("balance")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> BalanceAsync(DestinationChain destinationChain, string account)
        {
            try
            {
                if (!this.ethCompatibleClientProvider.IsChainSupportedAndEnabled(destinationChain))
                    return BadRequest($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                return Ok((await client.GetWStraxBalanceAsync(account).ConfigureAwait(false)).ToString());
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves the balance of a given account on a given ERC20 contract.
        /// </summary>
        /// <param name="destinationChain">The chain the ERC20 contract is deployed to.</param>
        /// <param name="account">The account to retrieve the balance for.</param>
        /// <param name="contractAddress">The address of the contract on the given chain.</param>
        /// <returns>The account balance.</returns>
        [Route("erc20balance")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> Erc20BalanceAsync(DestinationChain destinationChain, string account, string contractAddress)
        {
            try
            {
                if (!this.ethCompatibleClientProvider.IsChainSupportedAndEnabled(destinationChain))
                    return BadRequest($"{destinationChain} not enabled or supported!");

                IETHClient client = this.ethCompatibleClientProvider.GetClientForChain(destinationChain);

                return Ok((await client.GetErc20BalanceAsync(account, contractAddress).ConfigureAwait(false)).ToString());
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());

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
                return Ok($"{result} conversion requests have been deleted.");
            }

            return BadRequest($"Deleting conversion requests is only available on test networks.");
        }

        /// <summary>
        /// Endpoint that allows the multisig operator to set the state on a conversion request.
        /// </summary>
        /// <param name="model">The request details to set to.</param>
        [Route("requests/setstate")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult SetConversionRequestState([FromBody] SetConversionRequestStateModel model)
        {
            try
            {
                ConversionRequest request = this.conversionRequestRepository.Get(model.RequestId);

                if (request == null)
                    return NotFound($"'{model.RequestId}' does not exist.");

                if (this.chainIndexer.Tip.Height - request.BlockHeight <= this.network.Consensus.MaxReorgLength)
                    return BadRequest($"Please wait at least {this.network.Consensus.MaxReorgLength} blocks before attempting to update this request.");

                if (model.Processed && model.Status != ConversionRequestStatus.Processed)
                    return BadRequest($"A processed request must have its processed state set as true.");

                request.Processed = model.Processed;
                request.RequestStatus = model.Status;

                this.conversionRequestRepository.Save(request);

                return Ok($"Conversion request '{model.RequestId}' has been reset to {model.Status}.");
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception setting conversion request '{0}' to {1} : {2}.", model.RequestId, e.ToString(), model.Status);

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", e.Message);
            }
        }

        /// <summary>
        /// Endpoint that allows the multisig operator to reset the request as NotOriginator.
        /// </summary>
        /// <param name="model">The request id and height at which to reprocess the burn request at.</param>
        [Route("requests/reprocessburn")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult ReprocessBurnRequest([FromBody] ReprocessBurnRequestModel model)
        {
            try
            {
                this.conversionRequestRepository.ReprocessBurnRequest(model.RequestId, model.BlockHeight, ConversionRequestStatus.Unprocessed);
                this.logger.LogInformation($"Burn request '{model.RequestId}' will be reprocessed at height {model.BlockHeight}.");

                return Ok($"Burn request '{model.RequestId}' will be reprocessed at height {model.BlockHeight}.");
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception setting burn request '{0}' to be reprocessed : {1}.", model.RequestId, e.ToString());

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
                return Ok($"Manual vote pushed for request '{model.RequestId}' with event id '{model.EventId}'.");
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception manual pushing vote for conversion request '{0}' : {1}.", model.RequestId, e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", e.Message);
            }
        }

        /// <summary>
        /// Endpoint that allows the multisig operator to reset the scan height of the interop poller.
        /// </summary>
        /// <param name="model">The chain identifier and block height to reset the scan height for.</param>
        [Route("resetscanheight")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult ResetScanHeight([FromBody] ResetScanHeightModel model)
        {
            try
            {
                this.interopPoller.ResetScanHeight(model.DestinationChain, model.Height);
                this.logger.LogInformation($"Scan height for chain {model.DestinationChain} will be reset to '{model.Height}'.");

                return Ok($"Scan height for chain {model.DestinationChain} will be reset to '{model.Height}'.");
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception resetting scan height to '{0}' : {1}.", model.Height, e.ToString());

                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", e.Message);
            }
        }

        /// <summary>
        /// Endpoint that allows the user to decode the method parameters for an interflux transaction.
        /// </summary>
        /// <param name="hex">Hex of the interflux transaction.</param>
        [Route("decodeinterfluxtransaction")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult DecodeInterFluxTransaction(string hex)
        {
            try
            {
                Transaction transaction = this.network.CreateTransaction(hex);
                TxOut sc = transaction.Outputs.FirstOrDefault(x => x.ScriptPubKey.IsSmartContractExec());
                Result<ContractTxData> deserializedCallData = this.callDataSerializer.Deserialize(sc.ScriptPubKey.ToBytes());
                var methodParameters = deserializedCallData.Value.MethodParameters.Last() as byte[];
                var deserializedMethodParameters = this.contractPrimitiveSerializer.Deserialize<byte[][]>(methodParameters);

                Address address = this.contractPrimitiveSerializer.Deserialize<Address>(deserializedMethodParameters[0].Slice(1, (uint)(deserializedMethodParameters[0].Length - 1)));
                var addressString = address.ToUint160().ToBase58Address(this.network);

                UInt256 amount = this.contractPrimitiveSerializer.Deserialize<UInt256>(deserializedMethodParameters[1].Slice(1, (uint)(deserializedMethodParameters[1].Length - 1)));

                return Ok($"Method parameters for '{transaction.GetHash()}': address {addressString}; amount {amount}.");
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception trying to decode interflux transaction: {0}.", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Error", e.Message);
            }
        }

        private ConversionRequestModel ConstructConversionRequestModel(ConversionRequest request)
        {
            return new ConversionRequestModel()
            {
                RequestId = request.RequestId,
                RequestType = request.RequestType,
                RequestStatus = request.RequestStatus,
                BlockHeight = request.BlockHeight,
                DestinationAddress = request.DestinationAddress,
                DestinationChain = request.DestinationChain.ToString(),
                ExternalChainBlockHeight = request.ExternalChainBlockHeight,
                ExternalChainTxEventId = request.ExternalChainTxEventId,
                ExternalChainTxHash = request.ExternalChainTxHash,
                Amount = new BigInteger(request.Amount.ToBytes()),
                Processed = request.Processed,
                TokenContract = request.TokenContract,
                Status = request.RequestStatus.ToString(),
                Message = request.StatusMessage
            };
        }
    }
}
