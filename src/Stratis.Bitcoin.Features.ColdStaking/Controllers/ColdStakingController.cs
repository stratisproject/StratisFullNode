using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.ColdStaking.Models;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities.ModelStateErrors;

namespace Stratis.Bitcoin.Features.ColdStaking.Controllers
{
    /// <summary>
    /// Controller providing operations for cold staking.
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class ColdStakingController : Controller
    {
        public ColdStakingManager ColdStakingManager { get; private set; }
        
        private readonly IWalletTransactionHandler walletTransactionHandler;
        private readonly IWalletFeePolicy walletFeePolicy;
        private readonly IBroadcasterManager broadcasterManager;

        private readonly ILogger logger;

        public ColdStakingController(
            ILoggerFactory loggerFactory,
            IWalletManager walletManager,
            IWalletTransactionHandler walletTransactionHandler,
            IWalletFeePolicy walletFeePolicy,
            IBroadcasterManager broadcasterManager)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(walletManager, nameof(walletManager));
            Guard.NotNull(walletTransactionHandler, nameof(walletTransactionHandler));
            Guard.NotNull(walletFeePolicy, nameof(walletFeePolicy));
            Guard.NotNull(broadcasterManager, nameof(broadcasterManager));

            this.ColdStakingManager = walletManager as ColdStakingManager;
            Guard.NotNull(this.ColdStakingManager, nameof(this.ColdStakingManager));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.walletTransactionHandler = walletTransactionHandler;
            this.walletFeePolicy = walletFeePolicy;
            this.broadcasterManager = broadcasterManager;
        }

        /// <summary>
        /// Gets general information related to cold staking.
        /// </summary>
        /// <param name="request">A <see cref="GetColdStakingInfoRequest"/> object containing the
        /// parameters  required to obtain cold staking information.</param>
        /// <returns>A <see cref="GetColdStakingInfoResponse"/> object containing the cold staking information.</returns>
        /// <response code="200">Returns wallet cold staking info</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        /// <response code="500">Request is null</response>
        [Route("cold-staking-info")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GetColdStakingInfo([FromQuery]GetColdStakingInfoRequest request)
        {
            Guard.NotNull(request, nameof(request));

            this.logger.LogDebug("({0}:'{1}')", nameof(request), request);

            // Checks that the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                GetColdStakingInfoResponse model = this.ColdStakingManager.GetColdStakingInfo(request.WalletName);

                this.logger.LogTrace("(-):'{0}'", model);
                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Creates a cold staking account.
        /// </summary>
        /// <remarks>This method is used to create cold staking accounts on each machine/wallet, if required,
        /// prior to calling <see cref="GetColdStakingAddress"/>.</remarks>
        /// <param name="request">A <see cref="CreateColdStakingAccountRequest"/> object containing the parameters
        /// required for creating the cold staking account.</param>
        /// <returns>A <see cref="CreateColdStakingAccountResponse>"/> object containing the account name.</returns>
        /// <response code="200">Returns newly created account info</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        /// <response code="500">Request is null</response>
        [Route("cold-staking-account")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult CreateColdStakingAccount([FromBody]CreateColdStakingAccountRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks that the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                ExtPubKey extPubKey = null;

                try
                {
                    extPubKey = ExtPubKey.Parse(request.ExtPubKey);
                }
                catch
                {
                }

                var model = new CreateColdStakingAccountResponse
                {
                    AccountName = this.ColdStakingManager.GetOrCreateColdStakingAccount(request.WalletName, request.IsColdWalletAccount, request.WalletPassword, extPubKey).Name
                };

                this.logger.LogTrace("(-):'{0}'", model);
                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Gets a cold staking address. Assumes that the cold staking account exists.
        /// </summary>
        /// <remarks>This method is used to generate cold staking addresses on each machine/wallet
        /// which will then be used with <see cref="SetupColdStaking(SetupColdStakingRequest)"/>.</remarks>
        /// <param name="request">A <see cref="GetColdStakingAddressRequest"/> object containing the parameters
        /// required for generating the cold staking address.</param>
        /// <returns>A <see cref="GetColdStakingAddressResponse>"/> object containing the cold staking address.</returns>
        /// <response code="200">Returns cold staking address response</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        /// <response code="500">Request is null</response>
        [Route("cold-staking-address")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GetColdStakingAddress([FromQuery]GetColdStakingAddressRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks that the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                HdAddress address = this.ColdStakingManager.GetFirstUnusedColdStakingAddress(request.WalletName, request.IsColdWalletAddress);

                var model = new GetColdStakingAddressResponse
                {
                    Address = request.Segwit ? address?.Bech32Address : address?.Address
                };

                if (model.Address == null)
                    throw new WalletException("The cold staking account does not exist.");

                this.logger.LogTrace("(-):'{0}'", model);
                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Spends funds from a normal wallet addresses to the cold staking script. It is expected that this
        /// spend will be detected by both the hot wallet and cold wallet and allow cold staking to occur using this
        /// transaction's output as input.
        /// </summary>
        /// <param name="request">A <see cref="SetupColdStakingRequest"/> object containing the cold staking setup parameters.</param>
        /// <returns>A <see cref="SetupColdStakingResponse"/> object containing the hex representation of the transaction.</returns>
        /// <seealso cref="ColdStakingManager.GetColdStakingScript(ScriptId, ScriptId)"/>
        /// <response code="200">Returns setup transaction response</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        /// <response code="500">Request is null</response>
        [Route("setup-cold-staking")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult SetupColdStaking([FromBody]SetupColdStakingRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                Money amount = Money.Parse(request.Amount);
                Money feeAmount = Money.Parse(request.Fees);

                (Transaction transaction, _) = this.ColdStakingManager.GetColdStakingSetupTransaction(
                    this.walletTransactionHandler,
                    request.ColdWalletAddress,
                    request.HotWalletAddress,
                    request.WalletName,
                    request.WalletAccount,
                    request.WalletPassword,
                    amount,
                    feeAmount,
                    request.SubtractFeeFromAmount,
                    false,
                    request.SplitCount,
                    request.SegwitChangeAddress);

                var model = new SetupColdStakingResponse
                {
                    TransactionHex = transaction.ToHex()
                };

                this.logger.LogTrace("(-):'{0}'", model);
                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Creates a cold staking setup transaction in an unsigned state, so that the unsigned transaction can be transferred to
        /// an offline node that possesses the necessary private keys to sign it.
        /// </summary>
        /// <param name="request">A <see cref="SetupOfflineColdStakingRequest"/> object containing the cold staking setup parameters.</param>
        /// <returns>A <see cref="BuildOfflineSignResponse"/> object containing the hex representation of the unsigned transaction, as well as other metadata for offline signing.</returns>
        /// <response code="200">Returns offline setup transaction response</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        /// <response code="500">Request is null</response>
        [Route("setup-offline-cold-staking")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult SetupOfflineColdStaking([FromBody] SetupOfflineColdStakingRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                Money amount = Money.Parse(request.Amount);
                Money feeAmount = Money.Parse(request.Fees);

                (Transaction transaction, TransactionBuildContext context) = this.ColdStakingManager.GetColdStakingSetupTransaction(
                    this.walletTransactionHandler,
                    request.ColdWalletAddress,
                    request.HotWalletAddress,
                    request.WalletName,
                    request.WalletAccount,
                    null,
                    amount,
                    feeAmount,
                    request.SubtractFeeFromAmount,
                    true,
                    request.SplitCount,
                    request.SegwitChangeAddress);

                // TODO: We use the same code in the regular wallet for offline signing request construction, perhaps it should be moved to a common method
                // Need to be able to look up the keypath for the UTXOs that were used.
                IEnumerable<UnspentOutputReference> spendableTransactions = this.ColdStakingManager.GetSpendableTransactionsInAccount(
                        new WalletAccountReference(request.WalletName, request.WalletAccount)).ToList();

                var utxos = new List<UtxoDescriptor>();
                var addresses = new List<AddressDescriptor>();
                foreach (ICoin coin in context.TransactionBuilder.FindSpentCoins(transaction))
                {
                    utxos.Add(new UtxoDescriptor()
                    {
                        Amount = coin.TxOut.Value.ToUnit(MoneyUnit.BTC).ToString(),
                        TransactionId = coin.Outpoint.Hash.ToString(),
                        Index = coin.Outpoint.N.ToString(),
                        ScriptPubKey = coin.TxOut.ScriptPubKey.ToHex()
                    });

                    UnspentOutputReference outputReference = spendableTransactions.FirstOrDefault(u => u.Transaction.Id == coin.Outpoint.Hash && u.Transaction.Index == coin.Outpoint.N);

                    if (outputReference != null)
                    {
                        bool segwit = outputReference.Transaction.ScriptPubKey.IsScriptType(ScriptType.P2WPKH);
                        addresses.Add(new AddressDescriptor() { Address = segwit ? outputReference.Address.Bech32Address : outputReference.Address.Address, AddressType = segwit ? "p2wpkh" : "p2pkh", KeyPath = outputReference.Address.HdPath });
                    }
                }

                // Return transaction hex, UTXO list, address list. The offline signer will infer from the transaction structure that a cold staking setup is being made.
                var model = new BuildOfflineSignResponse()
                {
                    WalletName = request.WalletName,
                    WalletAccount = request.WalletAccount,
                    Fee = context.TransactionFee.ToUnit(MoneyUnit.BTC).ToString(),
                    UnsignedTransaction = transaction.ToHex(),
                    Utxos = utxos,
                    Addresses = addresses
                };

                this.logger.LogTrace("(-):'{0}'", model);
                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("estimate-cold-staking-setup-tx-fee")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult EstimateColdStakingSetupFee([FromBody] SetupColdStakingRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                Money amount = Money.Parse(request.Amount);

                Money estimatedFee = this.ColdStakingManager.EstimateSetupTransactionFee(
                    this.walletTransactionHandler,
                    request.ColdWalletAddress,
                    request.HotWalletAddress,
                    request.WalletName,
                    request.WalletAccount,
                    request.WalletPassword,
                    amount,
                    request.SubtractFeeFromAmount,
                    false,
                    request.SegwitChangeAddress,
                    request.SplitCount);

                this.logger.LogTrace("(-):'{0}'", estimatedFee);
                return this.Json(estimatedFee);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("estimate-offline-cold-staking-setup-tx-fee")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult EstimateOfflineColdStakingSetupFee([FromBody] SetupOfflineColdStakingRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                Money amount = Money.Parse(request.Amount);

                Money estimatedFee = this.ColdStakingManager.EstimateSetupTransactionFee(
                    this.walletTransactionHandler,
                    request.ColdWalletAddress,
                    request.HotWalletAddress,
                    request.WalletName,
                    request.WalletAccount,
                    null,
                    amount,
                    request.SubtractFeeFromAmount,
                    true,
                    request.SegwitChangeAddress,
                    request.SplitCount);

                this.logger.LogTrace("(-):'{0}'", estimatedFee);
                return this.Json(estimatedFee);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Spends funds from the cold staking wallet account back to a normal wallet addresses. It is expected that this
        /// spend will be detected by both the hot wallet and cold wallet and reduce the amount available for cold staking.
        /// </summary>
        /// <param name="request">A <see cref="ColdStakingWithdrawalRequest"/> object containing the cold staking withdrawal parameters.</param>
        /// <returns>A <see cref="ColdStakingWithdrawalResponse"/> object containing the hex representation of the transaction.</returns>
        /// <seealso cref="ColdStakingManager.GetColdStakingScript(ScriptId, ScriptId)"/>
        /// <response code="200">Returns withdrawal transaction response</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        /// <response code="500">Request is null</response>
        [Route("cold-staking-withdrawal")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult ColdStakingWithdrawal([FromBody]ColdStakingWithdrawalRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                Money amount = Money.Parse(request.Amount);
                Money feeAmount = Money.Parse(request.Fees);

                Transaction transaction = this.ColdStakingManager.GetColdStakingWithdrawalTransaction(this.walletTransactionHandler,
                    request.ReceivingAddress, request.WalletName, request.WalletPassword, amount, feeAmount, request.SubtractFeeFromAmount);

                var model = new ColdStakingWithdrawalResponse
                {
                    TransactionHex = transaction.ToHex()
                };

                this.logger.LogTrace("(-):'{0}'", model);

                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("offline-cold-staking-withdrawal")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult OfflineColdStakingWithdrawal([FromBody] OfflineColdStakingWithdrawalRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                Money amount = Money.Parse(request.Amount);
                Money feeAmount = Money.Parse(request.Fees);

                BuildOfflineSignResponse response = this.ColdStakingManager.BuildOfflineColdStakingWithdrawalRequest(this.walletTransactionHandler,
                    request.ReceivingAddress, request.WalletName, request.AccountName, amount, feeAmount, request.SubtractFeeFromAmount);

                this.logger.LogTrace("(-):'{0}'", response);

                return this.Json(response);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("estimate-offline-cold-staking-withdrawal-tx-fee")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult EstimateOfflineColdStakingWithdrawalFee([FromBody] OfflineColdStakingWithdrawalFeeEstimationRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                Money amount = Money.Parse(request.Amount);

                Money estimatedFee = this.ColdStakingManager.EstimateOfflineWithdrawalFee(this.walletTransactionHandler,
                    request.ReceivingAddress, request.WalletName, request.AccountName, amount, request.SubtractFeeFromAmount);

                this.logger.LogTrace("(-):'{0}'", estimatedFee);

                return this.Json(estimatedFee);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("estimate-cold-staking-withdrawal-tx-fee")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult EstimateColdStakingWithdrawalFee([FromBody] ColdStakingWithdrawalRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                Money amount = Money.Parse(request.Amount);

                Money estimatedFee = this.ColdStakingManager.EstimateWithdrawalTransactionFee(this.walletTransactionHandler,
                    request.ReceivingAddress, request.WalletName, amount, request.SubtractFeeFromAmount);

                this.logger.LogTrace("(-):'{0}'", estimatedFee);

                return this.Json(estimatedFee);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route("retrieve-filtered-utxos")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult RetrieveFilteredUtxos([FromBody] RetrieveFilteredUtxosRequest request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODEL_STATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                FeeRate feeRate = this.walletFeePolicy.GetFeeRate(FeeType.High.ToConfirmations());

                List<Transaction> retrievalTransactions = this.ColdStakingManager.RetrieveFilteredUtxos(request.WalletName, request.WalletPassword, request.Hex, feeRate, request.WalletAccount);

                if (request.Broadcast)
                {
                    foreach (Transaction transaction in retrievalTransactions)
                    {
                        this.broadcasterManager.BroadcastTransactionAsync(transaction);
                    }
                }

                return this.Json(retrievalTransactions.Select(t => t.ToHex()));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                this.logger.LogTrace("(-)[ERROR]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
