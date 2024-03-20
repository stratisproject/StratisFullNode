﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.Xml;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Interfaces;
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
        public const string GetTransferByDepositIdEndpoint = "transfer";
        public const string GetTransfersPartialEndpoint = "transfers/pending";
        public const string GetTransfersFullySignedEndpoint = "transfers/fullysigned";
        public const string GetTransfersSuspendedEndpoint = "transfers/suspended";
        public const string VerifyPartialTransactionEndpoint = "transfer/verify";
        public const string GetPartialTransactionSignersEndpoint = "transfer/signers";
        public const string UnsuspendTransactions = "unsuspend-transactions";
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
        private readonly IBlockStore blockStore;

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
            IBlockStore blockStore,
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
            this.blockStore = blockStore;
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
        public async Task<IActionResult> GetMaturedBlockDepositsAsync([FromQuery(Name = "blockHeight")] int blockHeight)
        {
            if (!this.ModelState.IsValid)
            {
                IEnumerable<string> errors = this.ModelState.Values.SelectMany(e => e.Errors.Select(m => m.ErrorMessage));
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Formatting error", string.Join(Environment.NewLine, errors));
            }

            try
            {
                SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await this.maturedBlocksProvider.RetrieveDepositsAsync(blockHeight).ConfigureAwait(false);
                return this.Json(depositsResult);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetMaturedBlockDeposits, e.Message);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, $"Could not re-sync matured block deposits: {e.Message}", e.ToString());
            }
        }

        [Route(FederationGatewayRouteEndPoint.GetTransfersSuspendedEndpoint)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GetTransfersSuspended([FromQuery(Name = "depositId")] string depositId = "", [FromQuery(Name = "transactionId")] string transactionId = "")
        {
            ICrossChainTransfer[] transfers = this.crossChainTransferStore.GetTransfersByStatus(new[] { CrossChainTransferStatus.Suspended }, false, false).ToArray();

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

        [Route(FederationGatewayRouteEndPoint.GetTransferByDepositIdEndpoint)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> GetTransferByDepositIdAsync([FromQuery(Name = "depositId")] string depositId)
        {
            if (!uint256.TryParse(depositId, out uint256 parsed))
                return this.Json($"{depositId} is not a valid deposit id.");

            ICrossChainTransfer[] transfers = await this.crossChainTransferStore.GetAsync(new[] { parsed }, false);

            if (transfers != null && transfers[0] == null)
                return this.Json($"{depositId} does not exist.");

            var model = new CrossChainTransferModel()
            {
                DepositAmount = transfers[0].DepositAmount,
                DepositId = transfers[0].DepositTransactionId,
                DepositHeight = transfers[0].DepositHeight,
                Transaction = new TransactionVerboseModel(transfers[0].PartialTransaction, this.network),
                TransferStatus = transfers[0].Status.ToString(),
            };

            return this.Json(model);
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
                this.logger.LogError("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetFederationMemberInfo, e.Message);
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
                this.logger.LogError("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.GetFederationInfo, e.Message);
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
                this.logger.LogError("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.FederationMemberIpAdd, e.Message);
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
                this.logger.LogError("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.FederationMemberIpRemove, e.Message);
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
                this.logger.LogError("Exception thrown calling /api/FederationGateway/{0}: {1}.", FederationGatewayRouteEndPoint.FederationMemberIpReplace, e.Message);
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

            ICrossChainTransfer[] transfers = await this.crossChainTransferStore.GetAsync(new[] { id }, false).ConfigureAwait(false);

            if (transfers != null && transfers.Any())
                return this.Json(this.federationWalletManager.ValidateTransaction(transfers[0].PartialTransaction, true));

            return this.Json($"{depositIdTransactionId} does not exist.");
        }

        [Route(FederationGatewayRouteEndPoint.DeleteSuspended)]
        [HttpDelete]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult DeleteSuspendedTransfers([FromBody] DeleteSuspendedTransferModel model)
        {
            (bool result, string message) deleteResult = this.crossChainTransferStore.DeleteSuspendedTransfer(new uint256(model.DepositId));
            if (deleteResult.result)
                return Ok($"'{model.DepositId}' was deleted.");

            return BadRequest(deleteResult.message);
        }

        [Route(FederationGatewayRouteEndPoint.GetPartialTransactionSignersEndpoint)]
        [HttpGet]
        public async Task<IActionResult> GetPartialTransactionSignersAsync([FromQuery(Name = "transactionId")] string transactionId, [FromQuery(Name = "input")] int input, [FromQuery(Name = "pubKeys")] bool pubKeys = false)
        {
            try
            {
                Guard.NotEmpty(transactionId, nameof(transactionId));

                uint256 txid;
                if (!uint256.TryParse(transactionId, out txid))
                {
                    throw new ArgumentException(nameof(transactionId));
                }

                ICrossChainTransfer[] cctx = await this.crossChainTransferStore.GetAsync(new[] { txid }).ConfigureAwait(false);

                Transaction trx = cctx.FirstOrDefault()?.PartialTransaction;

                if (trx == null)
                {
                    return this.Json(null);
                }

                uint256 prevOutHash = trx.Inputs[input].PrevOut.Hash;
                uint prevOutIndex = trx.Inputs[input].PrevOut.N;

                Transaction prevTrx = this.blockStore.GetTransactionById(prevOutHash);

                // Shouldn't be possible under normal circumstances.
                if (prevTrx == null)
                {
                    return this.Json(null);
                }

                TxOut txOut = prevTrx.Outputs[prevOutIndex];

                var txData = new PrecomputedTransactionData(trx);
                var checker = new TransactionChecker(trx, input, txOut.Value, txData);

                var ctx = new PartialTransactionScriptEvaluationContext(this.network)
                {
                    ScriptVerify = ScriptVerify.Mandatory
                };

                // Run the verification to populate the context, but don't actually check the result as this is only a partially signed transaction.
                ctx.VerifyScript(trx.Inputs[input].ScriptSig, txOut.ScriptPubKey, checker);

                var signers = new HashSet<string>();

                (PubKey[] transactionSigningKeys, int signaturesRequired) federation = this.network.Federations.GetOnlyFederation().GetFederationDetails();

                foreach (SignedHash signedHash in ctx.SignedHashes)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        PubKey pubKey = PubKey.RecoverFromSignature(i, signedHash.Signature.Signature, signedHash.Hash, true);

                        if (pubKey == null)
                            continue;

                        if (federation.transactionSigningKeys != null && federation.transactionSigningKeys.Contains(pubKey))
                        {
                            if (pubKeys)
                                signers.Add(pubKey.ToHex());
                            else
                                signers.Add(pubKey.GetAddress(this.network).ToString());
                        }
                    }
                }

                return this.Json(signers);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route(FederationGatewayRouteEndPoint.UnsuspendTransactions)]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public async Task<IActionResult> UnsuspendTransactionsAsync([FromBody] UnsuspendTransactionsModel request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                var model = new List<uint256>();

                foreach (TransactionToUnsuspend toUnsuspend in request.ToUnsuspend)
                {
                    this.logger.LogError("Attempting to unsuspend {0}", toUnsuspend.DepositId);

                    ICrossChainTransfer[] deposits = await this.crossChainTransferStore.GetAsync(new[] { toUnsuspend.DepositId }, false).ConfigureAwait(false);

                    if (deposits.Length == 0)
                        continue;

                    ICrossChainTransfer deposit = deposits[0];

                    if (deposit.Status != CrossChainTransferStatus.Suspended)
                        continue;

                    if (!string.IsNullOrEmpty(toUnsuspend.CounterChainDestination) && !string.IsNullOrEmpty(toUnsuspend.AmountToSend))
                    {
                        BitcoinAddress depositTargetAddress = BitcoinAddress.Create(toUnsuspend.CounterChainDestination, this.network);
                        Money depositAmount = Money.Parse(toUnsuspend.AmountToSend);
                        
                        deposit.SetPartialTransaction(this.network.Consensus.ConsensusFactory.CreateTransaction());

                        deposit.PartialTransaction.AddOutput(depositAmount, depositTargetAddress.ScriptPubKey);
                    }

                    /*
                    // For safety it is preferable that only Suspended transfers that have already-spent
                    // UTXOs in their partial transactions get unsuspended.
                    if (toUnsuspend.BlockHashContainingSpentUtxo != null)
                    {
                        bool alreadySpent = false;

                        Block block = this.blockStore.GetBlock(toUnsuspend.BlockHashContainingSpentUtxo);

                        foreach (Transaction transaction in block.Transactions)
                        {
                            if (alreadySpent)
                                break;

                            foreach (TxIn input in transaction.Inputs)
                            {
                                if (alreadySpent)
                                    break;

                                foreach (TxIn partialTransactionInput in deposit.PartialTransaction.Inputs)
                                {
                                    if (alreadySpent)
                                        break;

                                    if (input.PrevOut.Hash == partialTransactionInput.PrevOut.Hash && input.PrevOut.N == partialTransactionInput.PrevOut.N)
                                        alreadySpent = true;
                                }
                            }
                        }

                        if (alreadySpent)
                            continue;
                    }
                    */

                    this.crossChainTransferStore.ForceTransferStatusUpdate(deposit, CrossChainTransferStatus.Partial);

                    model.Add(deposit.DepositTransactionId);
                }

                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
