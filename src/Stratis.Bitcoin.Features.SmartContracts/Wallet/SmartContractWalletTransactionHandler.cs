using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.SmartContracts.Wallet
{
    public sealed class SmartContractWalletTransactionHandler : WalletTransactionHandler
    {
        public SmartContractWalletTransactionHandler(
            ILoggerFactory loggerFactory,
            IWalletManager walletManager,
            IWalletFeePolicy walletFeePolicy,
            Network network,
            StandardTransactionPolicy transactionPolicy,
            IReserveUtxoService utxoReservedService) :
            base(loggerFactory, walletManager, walletFeePolicy, network, transactionPolicy, utxoReservedService)
        {
        }

        /// <summary>
        /// The initialization of the builder is overridden as smart contracts calls allow dust and does not group
        /// inputs by ScriptPubKey.
        /// </summary>
        protected override void InitializeTransactionBuilder(TransactionBuildContext context)
        {
            Guard.NotNull(context, nameof(context));
            Guard.NotNull(context.Recipients, nameof(context.Recipients));
            Guard.NotNull(context.AccountReference, nameof(context.AccountReference));

            context.TransactionBuilder.CoinSelector = new DefaultCoinSelector
            {
                GroupByScriptPubKey = false
            };

            context.TransactionBuilder.DustPrevention = false;
            context.TransactionBuilder.StandardTransactionPolicy = this.TransactionPolicy;

            this.AddRecipients(context);
            this.AddOpReturnOutput(context);
            this.AddCoins(context);
            this.FindChangeAddress(context);
            this.AddFee(context);
        }

        /// <summary>
        /// Adjusted to allow smart contract transactions with zero value through.
        /// </summary>
        protected override void AddRecipients(TransactionBuildContext context)
        {
            if (context.Recipients.Any(recipient => recipient.Amount == Money.Zero && !recipient.ScriptPubKey.IsSmartContractExec()))
                throw new WalletException("No amount specified.");

            // TODO: The code duplication between this and the base handler is a bit unfortunate
            int totalSubtractingRecipients = context.Recipients.Count(r => r.SubtractFeeFromAmount);

            // If none of them need the fee subtracted then it's simply a matter of adding the individual recipients to the builder.
            if (totalSubtractingRecipients == 0)
            {
                foreach (Recipient recipient in context.Recipients)
                {
                    context.TransactionBuilder.Send(recipient.ScriptPubKey, recipient.Amount);
                }

                return;
            }

            // If the transaction fee has been explicitly specified, and we have any recipients that require a fee to be subtracted
            // from the amount to be sent, then evenly distribute the chosen fee among all recipients. Any remaining fee should be
            // subtracted from the first recipient.
            if (context.TransactionFee != null)
            {
                Money fee = context.TransactionFee;
                long recipientFee = fee.Satoshi / totalSubtractingRecipients;
                long remainingFee = fee.Satoshi % totalSubtractingRecipients;

                for (int i = 0; i < context.Recipients.Count; i++)
                {
                    Recipient recipient = context.Recipients[i];

                    if (recipient.SubtractFeeFromAmount)
                    {
                        // First receiver pays the remainder not divisible by output count.
                        long feeToSubtract = i == 0 ? remainingFee + recipientFee : recipientFee;
                        long remainingAmount = recipient.Amount.Satoshi - feeToSubtract;
                        if (remainingAmount <= 0)
                            throw new WalletException($"Fee {feeToSubtract} is higher than amount {recipient.Amount.Satoshi} to send.");

                        recipient.Amount = new Money(remainingAmount);
                    }

                    context.TransactionBuilder.Send(recipient.ScriptPubKey, recipient.Amount);
                }
            }
            else
            {
                // This is currently a limitation of the NBitcoin TransactionBuilder.
                // The only alternative would possibly be to recompute the output sizes after the AddFee call.
                if (totalSubtractingRecipients > 1)
                    throw new WalletException($"Cannot subtract fee from more than 1 recipient if {nameof(context.TransactionFee)} is not set.");

                // If the transaction fee has not been explicitly specified yet, then the builder needs to assign it later from the wallet fee policy.
                // So we just need to indicate to the builder that the fees must be subtracted from the specified recipient.
                foreach (Recipient recipient in context.Recipients)
                {
                    context.TransactionBuilder.Send(recipient.ScriptPubKey, recipient.Amount);

                    if (recipient.SubtractFeeFromAmount)
                        context.TransactionBuilder.SubtractFees();
                }
            }
        }
    }
}
