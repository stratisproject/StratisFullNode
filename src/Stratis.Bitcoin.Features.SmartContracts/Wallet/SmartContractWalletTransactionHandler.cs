using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.SmartContracts.Wallet
{
    public sealed class SmartContractWalletTransactionHandler : WalletTransactionHandler
    {
        private readonly Network network;
        private readonly ISmartContractPosActivationProvider smartContractPosActivationProvider;

        public SmartContractWalletTransactionHandler(
            ILoggerFactory loggerFactory,
            IWalletManager walletManager,
            IWalletFeePolicy walletFeePolicy,
            Network network,
            StandardTransactionPolicy transactionPolicy,
            IReserveUtxoService utxoReservedService, 
            ISmartContractPosActivationProvider smartContractPosActivationProvider = null /* Only for PoS */) :
            base(loggerFactory, walletManager, walletFeePolicy, network, transactionPolicy, utxoReservedService)
        {
            this.network = network;
            this.smartContractPosActivationProvider = smartContractPosActivationProvider;
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

            if (this.network.Consensus.ConsensusFactory is SmartContractPoSConsensusFactory)
            {
                // Just revert to legacy PoS behavior until SC active.
                base.InitializeTransactionBuilder(context);
                if (this.smartContractPosActivationProvider.IsActive(null))
                {
                    context.TransactionBuilder.StandardTransactionPolicy = this.TransactionPolicy;
                }

                return;
            }

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
       
    }
}
