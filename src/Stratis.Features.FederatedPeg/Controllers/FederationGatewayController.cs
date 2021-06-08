using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NLog;
using Stratis.Bitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.Extensions;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.SourceChain;

namespace Stratis.Features.FederatedPeg.Controllers
{
    public static class FederationGatewayRouteEndPoint
    {
        public const string DeleteSuspended = "transfers/deletesuspended";
        public const string GetMaturedBlockDeposits = "deposits";
        public const string GetFederationInfo = "info";
        public const string GetFederationMemberInfo = "member/info";
        public const string FederationMemberIpAdd = "member/ip/add";
        public const string FederationMemberIpRemove = "member/ip/remove";
        public const string FederationMemberIpReplace = "member/ip/replace";
        public const string GetTransfersPartialEndpoint = "transfer/pending";
        public const string GetTransfersFullySignedEndpoint = "transfer/fullysigned";
        public const string VerifyPartialTransactionEndpoint = "transfer/verify";
    }

    /// <summary>
    /// API used to communicate across to the counter chain.
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class FederationGatewayController : Controller
    {
        private readonly IAsyncProvider asyncProvider;
        private readonly ChainIndexer chainIndexer;
        private readonly IConnectionManager connectionManager;
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly IFederationWalletManager federationWalletManager;
        private readonly IFederationManager federationManager;
        private readonly IFullNode fullNode;
        private readonly ILogger logger;
        private readonly IMaturedBlocksProvider maturedBlocksProvider;
        private readonly Network network;
        private readonly IPeerBanning peerBanning;

        public FederationGatewayController(
            IAsyncProvider asyncProvider,
            ChainIndexer chainIndexer,
            IConnectionManager connectionManager,
            ICrossChainTransferStore crossChainTransferStore,
            IMaturedBlocksProvider maturedBlocksProvider,
            Network network,
            IFederatedPegSettings federatedPegSettings,
            IFederationWalletManager federationWalletManager,
            IFullNode fullNode,
            IPeerBanning peerBanning,
            IFederationManager federationManager = null)
        {
            this.asyncProvider = asyncProvider;
            this.chainIndexer = chainIndexer;
            this.connectionManager = connectionManager;
            this.crossChainTransferStore = crossChainTransferStore;
            this.federatedPegSettings = federatedPegSettings;
            this.federationWalletManager = federationWalletManager;
            this.federationManager = federationManager;
            this.fullNode = fullNode;
            this.logger = LogManager.GetCurrentClassLogger();
            this.maturedBlocksProvider = maturedBlocksProvider;
            this.network = network;
            this.peerBanning = peerBanning;
        }

        /// <summary>
        /// Retrieves blocks deposits.
        /// </summary>
        /// <param name="blockHeight">Last known block height at which to retrieve from.</param>
        /// <returns><see cref="IActionResult"/>OK on success.</returns>
        /// <response code="200">Returns blocks deposits</response>
        /// <response code="400">Invalid request or blocks are not mature</response>
        /// <response code="500">Request is null</response>
        [Route(FederationGatewayRouteEndPoint.GetMaturedBlockDeposits)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GetMaturedBlockDeposits([FromQuery(Name = "blockHeight")] int blockHeight)
        {
            if (!this.ModelState.IsValid)
            {
                IEnumerable<string> errors = this.ModelState.Values.SelectMany(e => e.Errors.Select(m => m.ErrorMessage));
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Formatting error", string.Join(Environment.NewLine, errors));
            }

            try
            {
                SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = this.maturedBlocksProvider.RetrieveDeposits(blockHeight);
                return this.Json(depositsResult);
            }
            catch (Exception e)
            {
                this.logger.Error("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetMaturedBlockDeposits, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, $"Could not re-sync matured block deposits: {e.Message}", e.ToString());
            }
        }

        [Route(FederationGatewayRouteEndPoint.GetTransfersPartialEndpoint)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GetTransfersPending([FromQuery(Name = "depositId")] string depositId = "", [FromQuery(Name = "transactionId")] string transactionId = "")
        {
            ICrossChainTransfer[] transfers = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.Partial }, false, false).ToArray();

            CrossChainTransferModel[] transactions = transfers
                .Where(t => ((string.IsNullOrEmpty(depositId) && string.IsNullOrEmpty(transactionId)) || (t.DepositTransactionId.ToString().StartsWith(depositId) && (t.PartialTransaction == null || t.PartialTransaction.GetHash().ToString().StartsWith(transactionId)))))
                .Select(t => new CrossChainTransferModel()
                {
                    DepositAmount = t.DepositAmount,
                    DepositId = t.DepositTransactionId,
                    DepositHeight = t.DepositHeight,
                    Transaction = new TransactionVerboseModel(t.PartialTransaction, this.network),
                    TransferStatus = t.Status.ToString(),
                }).ToArray();

            return this.Json(transactions);
        }

        [Route(FederationGatewayRouteEndPoint.GetTransfersFullySignedEndpoint)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GetTransfers([FromQuery(Name = "depositId")] string depositId = "", [FromQuery(Name = "transactionId")] string transactionId = "")
        {
            ICrossChainTransfer[] transfers = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.FullySigned }, false, false).ToArray();

            CrossChainTransferModel[] transactions = transfers
                .Where(t => (string.IsNullOrEmpty(depositId) && string.IsNullOrEmpty(transactionId)) || (t.DepositTransactionId.ToString().StartsWith(depositId) && (t.PartialTransaction == null || t.PartialTransaction.GetHash().ToString().StartsWith(transactionId))))
                .Select(t => new CrossChainTransferModel()
                {
                    DepositAmount = t.DepositAmount,
                    DepositId = t.DepositTransactionId,
                    DepositHeight = t.DepositHeight,
                    Transaction = new TransactionVerboseModel(t.PartialTransaction, this.network),
                    TransferStatus = t.Status.ToString(),
                }).ToArray();

            return this.Json(transactions);
        }

        /// <summary>
        /// Gets info on the state of a multisig member.
        /// </summary>
        /// <returns>A <see cref="FederationMemberInfoModel"/> with information about the federation member.</returns>
        /// <response code="200">Returns federation member info.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(FederationGatewayRouteEndPoint.GetFederationMemberInfo)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetFederationMemberInfo()
        {
            try
            {
                var infoModel = new FederationMemberInfoModel
                {
                    AsyncLoopState = this.asyncProvider.GetStatistics(true, true),
                    ConsensusHeight = this.chainIndexer.Tip.Height,
                    CrossChainStoreHeight = this.crossChainTransferStore.TipHashAndHeight.Height,
                    CrossChainStoreNextDepositHeight = this.crossChainTransferStore.NextMatureDepositHeight,
                    CrossChainStorePartialTxs = this.crossChainTransferStore.GetTransferCountByStatus(CrossChainTransferStatus.Partial),
                    CrossChainStoreSuspendedTxs = this.crossChainTransferStore.GetTransferCountByStatus(CrossChainTransferStatus.Suspended),
                    FederationWalletActive = this.federationWalletManager.IsFederationWalletActive(),
                    FederationWalletHeight = this.federationWalletManager.WalletTipHeight,
                    NodeVersion = this.fullNode.Version?.ToString() ?? "0",
                    PubKey = this.federationManager?.CurrentFederationKey?.PubKey?.ToHex(),
                };

                foreach (IPEndPoint federationIpEndpoints in this.federatedPegSettings.FederationNodeIpEndPoints)
                {
                    var federationMemberConnection = new FederationMemberConnectionInfo() { FederationMemberIp = federationIpEndpoints.ToString() };

                    INetworkPeer peer = this.connectionManager.FindNodeByEndpoint(federationIpEndpoints);

                    if (peer != null)
                    {
                        if (peer.IsConnected)
                            federationMemberConnection.Connected = true;

                        if (this.peerBanning.IsBanned(peer.PeerEndPoint))
                            federationMemberConnection.IsBanned = true;
                    }

                    infoModel.FederationMemberConnections.Add(federationMemberConnection);
                }

                infoModel.FederationConnectionState = $"{infoModel.FederationMemberConnections.Count(f => f.Connected)} out of {infoModel.FederationMemberConnections.Count}";

                return this.Json(infoModel);
            }
            catch (Exception e)
            {
                this.logger.Error("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetFederationMemberInfo, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Gets some info on the state of the federation.
        /// </summary>
        /// <returns>A <see cref="FederationGatewayInfoModel"/> with information about the federation.</returns>
        /// <response code="200">Returns federation info</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(FederationGatewayRouteEndPoint.GetFederationInfo)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetInfo()
        {
            try
            {
                bool isMainchain = this.federatedPegSettings.IsMainChain;

                var model = new FederationGatewayInfoModel
                {
                    IsActive = this.federationWalletManager.IsFederationWalletActive(),
                    IsMainChain = isMainchain,
                    FederationNodeIpEndPoints = this.federatedPegSettings.FederationNodeIpEndPoints.Select(i => $"{i.Address}:{i.Port}"),
                    MultisigPublicKey = this.federatedPegSettings.PublicKey,
                    FederationMultisigPubKeys = this.federatedPegSettings.FederationPublicKeys.Select(k => k.ToString()),
                    MiningPublicKey = isMainchain ? null : this.federationManager?.CurrentFederationKey?.PubKey.ToString(),
                    FederationMiningPubKeys = isMainchain ? null : this.federationManager?.GetFederationMembers().Select(k => k.ToString()),
                    MultiSigAddress = this.federatedPegSettings.MultiSigAddress,
                    MultiSigRedeemScript = this.federatedPegSettings.MultiSigRedeemScript.ToString(),
                    MultiSigRedeemScriptPaymentScript = this.federatedPegSettings.MultiSigRedeemScript.PaymentScript.ToString(),
                    MinimumDepositConfirmationsSmallDeposits = (uint)this.federatedPegSettings.MinimumConfirmationsSmallDeposits,
                    MinimumDepositConfirmationsNormalDeposits = (uint)this.federatedPegSettings.MinimumConfirmationsNormalDeposits,
                    MinimumDepositConfirmationsLargeDeposits = (uint)this.federatedPegSettings.MinimumConfirmationsLargeDeposits,
                    MinimumDepositConfirmationsDistributionDeposits = (uint)this.federatedPegSettings.MinimumConfirmationsDistributionDeposits
                };

                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.Error("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetFederationInfo, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Adds a federation member's IP address to the federation IP list.
        /// </summary>
        /// <response code="200">The federation member's IP was successfully added.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(FederationGatewayRouteEndPoint.FederationMemberIpAdd)]
        [HttpPut]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult AddFederationMemberIp([FromBody] FederationMemberIpModel model)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            try
            {
                IPEndPoint endPoint = model.EndPoint.ToIPEndPoint(this.fullNode.Network.DefaultPort);

                if (this.federatedPegSettings.FederationNodeIpEndPoints.Contains(endPoint))
                    return this.Json($"{endPoint} already exists in the federation.");

                this.federatedPegSettings.FederationNodeIpEndPoints.Add(endPoint);
                this.federatedPegSettings.FederationNodeIpAddresses.Add(endPoint.Address);

                return this.Json($"{endPoint} has been added.");
            }
            catch (Exception e)
            {
                this.logger.Error("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.FederationMemberIpAdd, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Remove's a federation member's IP address from the federation IP list.
        /// </summary>
        /// <response code="200">The federation member's IP was successfully removed.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(FederationGatewayRouteEndPoint.FederationMemberIpRemove)]
        [HttpPut]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult RemoveFederationMemberIp([FromBody] FederationMemberIpModel model)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            try
            {
                IPEndPoint endPoint = model.EndPoint.ToIPEndPoint(this.fullNode.Network.DefaultPort);

                if (!this.federatedPegSettings.FederationNodeIpEndPoints.Contains(endPoint))
                    return this.Json($"{endPoint} does not exist in the federation.");

                this.federatedPegSettings.FederationNodeIpEndPoints.Remove(endPoint);
                this.federatedPegSettings.FederationNodeIpAddresses.Remove(endPoint.Address);

                return this.Json($"{endPoint} has been removed.");
            }
            catch (Exception e)
            {
                this.logger.Error("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.FederationMemberIpRemove, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Replaces a federation member's IP address from the federation IP list with a new one.
        /// </summary>
        /// <response code="200">The federation member's IP was successfully replaced.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(FederationGatewayRouteEndPoint.FederationMemberIpReplace)]
        [HttpPut]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult ReplaceFederationMemberIp([FromBody] ReplaceFederationMemberIpModel model)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            try
            {
                IPEndPoint endPointToReplace = model.EndPoint.ToIPEndPoint(this.fullNode.Network.DefaultPort);
                IPEndPoint endPointToUse = model.EndPointToUse.ToIPEndPoint(this.fullNode.Network.DefaultPort);

                if (!this.federatedPegSettings.FederationNodeIpEndPoints.Contains(endPointToReplace))
                    return this.Json($"{endPointToReplace} does not exist in the federation.");

                this.federatedPegSettings.FederationNodeIpEndPoints.Remove(endPointToReplace);
                this.federatedPegSettings.FederationNodeIpAddresses.Remove(endPointToReplace.Address);

                if (this.federatedPegSettings.FederationNodeIpEndPoints.Contains(endPointToUse))
                    return this.Json($"{endPointToUse} already exists in the federation.");

                this.federatedPegSettings.FederationNodeIpEndPoints.Add(endPointToUse);
                this.federatedPegSettings.FederationNodeIpAddresses.Add(endPointToUse.Address);

                return this.Json($"{endPointToReplace} has been replaced with {endPointToUse}.");
            }
            catch (Exception e)
            {
                this.logger.Error("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.FederationMemberIpReplace, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route(FederationGatewayRouteEndPoint.VerifyPartialTransactionEndpoint)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> VerifyPartialTransactionAsync([FromQuery(Name = "depositIdTransactionId")] string depositIdTransactionId)
        {
            if (string.IsNullOrEmpty("depositIdTransactionId"))
                return this.Json("Deposit transaction id not specified.");

            if (!uint256.TryParse(depositIdTransactionId, out uint256 id))
                return this.Json("Invalid deposit transaction id");

            ICrossChainTransfer[] transfers = await this.crossChainTransferStore.GetAsync(new[] { id }, false);

            if (transfers != null && transfers.Any())
                return this.Json(this.federationWalletManager.ValidateTransaction(transfers[0].PartialTransaction, true));

            return this.Json($"{depositIdTransactionId} does not exist.");
        }

        [Route(FederationGatewayRouteEndPoint.DeleteSuspended)]
        [HttpDelete]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult DeleteSuspendedTransfers()
        {
            if (this.network.IsTest() || this.network.IsRegTest())
            {
                var result = this.crossChainTransferStore.DeleteSuspendedTransfers();
                return this.Json($"{result} suspended transfers has been removed.");
            }

            return this.Json($"Deleting suspended transfers is only available on test networks.");
        }
    }
}
