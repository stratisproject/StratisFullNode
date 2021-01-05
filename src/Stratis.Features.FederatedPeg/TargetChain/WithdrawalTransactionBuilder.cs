using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Signals;
using Stratis.Features.FederatedPeg.Distribution;
using Stratis.Features.FederatedPeg.Events;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Wallet;
using Recipient = Stratis.Features.FederatedPeg.Wallet.Recipient;
using TransactionBuildContext = Stratis.Features.FederatedPeg.Wallet.TransactionBuildContext;

namespace Stratis.Features.FederatedPeg.TargetChain
{
    public class WithdrawalTransactionBuilder : IWithdrawalTransactionBuilder
    {
        /// <summary>
        /// The wallet should always consume UTXOs that have already been seen in a block. This makes it much easier to maintain
        /// determinism across the wallets on all the nodes.
        /// </summary>
        public const int MinConfirmations = 1;

        private readonly ILogger logger;
        private readonly Network network;

        private readonly Script cirrusRewardDummyAddressScriptPubKey;
        private readonly IFederationWalletManager federationWalletManager;
        private readonly IFederationWalletTransactionHandler federationWalletTransactionHandler;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ISignals signals;
        private readonly IRewardDistributionManager distributionManager;
        private int previousDistributionHeight;

        public WithdrawalTransactionBuilder(
            ILoggerFactory loggerFactory,
            Network network,
            IFederationWalletManager federationWalletManager,
            IFederationWalletTransactionHandler federationWalletTransactionHandler,
            IFederatedPegSettings federatedPegSettings,
            ISignals signals,
            IRewardDistributionManager distributionManager = null)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.federationWalletManager = federationWalletManager;
            this.federationWalletTransactionHandler = federationWalletTransactionHandler;
            this.federatedPegSettings = federatedPegSettings;
            this.signals = signals;
            this.distributionManager = distributionManager;

            this.cirrusRewardDummyAddressScriptPubKey = BitcoinAddress.Create(this.network.CirrusRewardDummyAddress).ScriptPubKey;
            this.previousDistributionHeight = 0;
        }

        /// <inheritdoc />
        public Transaction BuildWithdrawalTransaction(int blockHeight, uint256 depositId, uint blockTime, Recipient recipient)
        {
            try
            {
                this.logger.LogDebug("BuildDeterministicTransaction depositId(opReturnData)={0}; recipient.ScriptPubKey={1}; recipient.Amount={2}; height={3}", depositId, recipient.ScriptPubKey, recipient.Amount, blockHeight);

                // Build the multisig transaction template.
                uint256 opReturnData = depositId;
                string walletPassword = this.federationWalletManager.Secret.WalletPassword;
                bool sign = (walletPassword ?? "") != "";

                var multiSigContext = new TransactionBuildContext(new List<Recipient>(), opReturnData: opReturnData.ToBytes())
                {
                    MinConfirmations = MinConfirmations,
                    Shuffle = false,
                    IgnoreVerify = true,
                    WalletPassword = walletPassword,
                    Sign = sign,
                    Time = this.network.Consensus.IsProofOfStake ? blockTime : (uint?)null
                };

                multiSigContext.Recipients = new List<Recipient> { recipient.WithPaymentReducedByFee(FederatedPegSettings.CrossChainTransferFee) };

                // Withdrawals from the sidechain won't have the OP_RETURN transaction tag, so we need to check against the ScriptPubKey of the Cirrus Dummy address.
                if (!this.federatedPegSettings.IsMainChain && recipient.ScriptPubKey.Length > 0 && recipient.ScriptPubKey == this.cirrusRewardDummyAddressScriptPubKey)
                {
                    // Use the distribution manager to determine the actual list of recipients.
                    // TODO: This would probably be neater if it was moved to the CCTS with the current method accepting a list of recipients instead
                    if (this.previousDistributionHeight != blockHeight)
                    {
                        multiSigContext.Recipients = this.distributionManager.Distribute(blockHeight, recipient.WithPaymentReducedByFee(FederatedPegSettings.CrossChainTransferFee).Amount); // Reduce the overall amount by the fee first before splitting it up.

                        // This can be transient as it is just to stop distribution happening multiple times
                        // on blocks that contain more than one deposit.
                        this.previousDistributionHeight = blockHeight;
                    }
                }

                // TODO: Amend this so we're not picking coins twice.
                (List<Coin> coins, List<Wallet.UnspentOutputReference> unspentOutputs) = FederationWalletTransactionHandler.DetermineCoins(this.federationWalletManager, this.network, multiSigContext, this.federatedPegSettings);

                if (coins.Count > FederatedPegSettings.MaxInputs)
                {
                    this.logger.LogDebug("Too many inputs. Triggering the consolidation process.");
                    this.signals.Publish(new WalletNeedsConsolidation(recipient.Amount));
                    this.logger.LogTrace("(-)[CONSOLIDATING_INPUTS]");
                    return null;
                }

                multiSigContext.TransactionFee = this.federatedPegSettings.GetWithdrawalTransactionFee(coins.Count); // The "actual fee". Everything else goes to the fed.
                multiSigContext.SelectedInputs = unspentOutputs.Select(u => u.ToOutPoint()).ToList();
                multiSigContext.AllowOtherInputs = false;

                // Build the transaction.
                Transaction transaction = this.federationWalletTransactionHandler.BuildTransaction(multiSigContext);

                this.logger.LogDebug("transaction = {0}", transaction.ToString(this.network, RawFormat.BlockExplorer));

                return transaction;
            }
            catch (Exception error)
            {
                if (error is WalletException walletException &&
                    (walletException.Message == FederationWalletTransactionHandler.NoSpendableTransactionsMessage
                     || walletException.Message == FederationWalletTransactionHandler.NotEnoughFundsMessage))
                {
                    this.logger.LogWarning("Not enough spendable transactions in the wallet. Should be resolved when a pending transaction is included in a block.");
                }
                else
                {
                    this.logger.LogError("Could not create transaction for deposit {0}: {1}", depositId, error.Message);
                }
            }

            this.logger.LogTrace("(-)[FAIL]");
            return null;
        }
    }
}
