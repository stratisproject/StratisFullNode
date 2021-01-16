using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.P2P.Peer;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.SourceChain;

namespace Stratis.Features.FederatedPeg.Controllers
{
    public static class FederationGatewayRouteEndPoint
    {
        public const string GetMaturedBlockDeposits = "deposits";
        public const string GetFederationInfo = "info";
        public const string GetTransfers = "gettransfers";
        public const string GetFederationMemberInfo = "info/member";
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

        public FederationGatewayController(
            IAsyncProvider asyncProvider,
            ChainIndexer chainIndexer,
            IConnectionManager connectionManager,
            ICrossChainTransferStore crossChainTransferStore,
            ILoggerFactory loggerFactory,
            IMaturedBlocksProvider maturedBlocksProvider,
            Network network,
            IFederatedPegSettings federatedPegSettings,
            IFederationWalletManager federationWalletManager,
            IFullNode fullNode,
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
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.maturedBlocksProvider = maturedBlocksProvider;
            this.network = network;
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
                this.logger.LogDebug("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetMaturedBlockDeposits, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, $"Could not re-sync matured block deposits: {e.Message}", e.ToString());
            }
        }

        [Route(FederationGatewayRouteEndPoint.GetTransfers)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GetTransfers([FromQuery(Name = "depositId")] string depositId = "", [FromQuery(Name = "transactionId")] string transactionId = "", [FromQuery(Name = "amount")] int? amount = 100)
        {
            ICrossChainTransfer[] transfers = this.crossChainTransferStore.GetTransfersByStatus(new[] {
                CrossChainTransferStatus.FullySigned,
                CrossChainTransferStatus.Partial,
                CrossChainTransferStatus.SeenInBlock })
                .ToArray();

            CrossChainTransferModel[] transactions = transfers
                .Where(t => t.PartialTransaction != null)
                .Where(t => t.DepositTransactionId.ToString().StartsWith(depositId) && (t.PartialTransaction == null || t.PartialTransaction.GetHash().ToString().StartsWith(transactionId)))
                .Select(t => new CrossChainTransferModel()
                {
                    DepositAmount = t.DepositAmount,
                    DepositId = t.DepositTransactionId,
                    DepositHeight = t.DepositHeight,
                    Transaction = new TransactionVerboseModel(t.PartialTransaction, this.network),
                    TransferStatus = t.Status.ToString(),
                }).ToArray();

            return this.Json(transactions.OrderByDescending(t => t.Transaction.BlockTime).Take(amount.Value));
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
                    if (peer != null && peer.IsConnected)
                        federationMemberConnection.Connected = true;

                    infoModel.FederationMemberConnections.Add(federationMemberConnection);
                }

                infoModel.FederationConnectionState = $"{infoModel.FederationMemberConnections.Count(f => f.Connected)} out of {infoModel.FederationMemberConnections.Count}";

                return this.Json(infoModel);
            }
            catch (Exception e)
            {
                this.logger.LogDebug("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetFederationMemberInfo, e.Message);
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
                this.logger.LogDebug("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetFederationInfo, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
