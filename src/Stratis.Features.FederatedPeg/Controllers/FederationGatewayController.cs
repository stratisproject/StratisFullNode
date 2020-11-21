using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.SourceChain;

namespace Stratis.Features.FederatedPeg.Controllers
{
    public static class FederationGatewayRouteEndPoint
    {
        public const string GetFasterMaturedBlockDeposits = "deposits/faster";
        public const string GetMaturedBlockDeposits = "deposits";
        public const string GetInfo = "info";
        public const string GetTransfer = "gettransfers";
    }

    /// <summary>
    /// API used to communicate across to the counter chain.
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class FederationGatewayController : Controller
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        private readonly IMaturedBlocksProvider maturedBlocksProvider;

        private readonly IFederatedPegSettings federatedPegSettings;

        private readonly IFederationWalletManager federationWalletManager;

        private readonly IFederationManager federationManager;

        private readonly ICrossChainTransferStore crossChainTransferStore;

        public FederationGatewayController(
            ILoggerFactory loggerFactory,
            IMaturedBlocksProvider maturedBlocksProvider,
            IFederatedPegSettings federatedPegSettings,
            IFederationWalletManager federationWalletManager,
            IFederationManager federationManager = null,
            ICrossChainTransferStore crossChainTransferStore = null)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.maturedBlocksProvider = maturedBlocksProvider;
            this.federatedPegSettings = federatedPegSettings;
            this.federationWalletManager = federationWalletManager;
            this.federationManager = federationManager;
            this.crossChainTransferStore = crossChainTransferStore;
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
        public IActionResult GetMaturedBlockDeposits([FromQuery(Name = "h")] int blockHeight)
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

        [Route(FederationGatewayRouteEndPoint.GetTransfer)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GetTransfer([FromQuery(Name = "depositId")] string depositId = "", [FromQuery(Name = "transactionId")] string transactionId = "")
        {
            ICrossChainTransfer[] transfers = this.crossChainTransferStore.GetTransfersByStatus(new[] {
                CrossChainTransferStatus.FullySigned,
                CrossChainTransferStatus.Partial,
                CrossChainTransferStatus.SeenInBlock })
                .ToArray();

            var transactions = transfers
                .Where(t => new[] { CrossChainTransferStatus.FullySigned, CrossChainTransferStatus.Partial, CrossChainTransferStatus.SeenInBlock }.Contains(t.Status))
                .Where(t => t.PartialTransaction != null)
                .Where(t => t.DepositTransactionId.ToString().StartsWith(depositId) && (t.PartialTransaction == null || t.PartialTransaction.GetHash().ToString().StartsWith(transactionId)))
                .Select(t => $"DepositId = {t.DepositTransactionId}, TransactionId = { t.PartialTransaction.GetHash() }, Transaction = { t.PartialTransaction.ToHex() }")
                .ToArray();

            return this.Json(transactions);
        }

        /// <summary>
        /// Gets some info on the state of the federation.
        /// </summary>
        /// <returns>A <see cref="FederationGatewayInfoModel"/> with information about the federation.</returns>
        /// <response code="200">Returns federation info</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(FederationGatewayRouteEndPoint.GetInfo)]
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
                    MiningPublicKey = isMainchain ? null : this.federationManager.CurrentFederationKey?.PubKey.ToString(),
                    FederationMiningPubKeys = isMainchain ? null : this.federationManager.GetFederationMembers().Select(k => k.ToString()),
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
                this.logger.LogDebug("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetInfo, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Builds an <see cref="IActionResult"/> containing errors contained in the <see cref="ControllerBase.ModelState"/>.
        /// </summary>
        /// <returns>A result containing the errors.</returns>
        private static IActionResult BuildErrorResponse(ModelStateDictionary modelState)
        {
            List<ModelError> errors = modelState.Values.SelectMany(e => e.Errors).ToList();
            return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest,
                string.Join(Environment.NewLine, errors.Select(m => m.ErrorMessage)),
                string.Join(Environment.NewLine, errors.Select(m => m.Exception?.Message)));
        }
    }
}
