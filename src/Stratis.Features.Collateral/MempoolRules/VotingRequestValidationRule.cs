using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.PoA.Features.Voting;

namespace Stratis.Bitcoin.Features.Collateral.MempoolRules
{
    public class VotingRequestValidationRule : MempoolRule
    {
        private readonly JoinFederationRequestEncoder encoder;
        private readonly VotingManager votingManager;
        private readonly IFederationManager federationManager;

        public VotingRequestValidationRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory,
            VotingManager votingManager,
            IFederationManager federationManager) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
            this.encoder = new JoinFederationRequestEncoder(loggerFactory);
            this.votingManager = votingManager;
            this.federationManager = federationManager;
        }

        /// <inheritdoc/>
        public override void CheckTransaction(MempoolValidationContext context)
        {
            if (context.Transaction.IsCoinBase || context.Transaction.IsCoinStake)
                return;

            // This will raise a consensus error if there is a voting request and it can't be decoded.
            JoinFederationRequest request = JoinFederationRequestBuilder.Deconstruct(context.Transaction, this.encoder);
            if (request == null)
                return;

            if (this.federationManager.IsMultisigMember(request.PubKey))
            {
                this.logger.LogTrace("(-)[INVALID_MULTISIG_VOTING]");
                context.State.Fail(MempoolErrors.VotingRequestInvalidMultisig, $"{context.Transaction.GetHash()} has an invalid voting request for a multisig member.").Throw();
            }

            // Check collateral amount.
            var collateralAmount = CollateralFederationMember.GetCollateralAmountForPubKey((PoANetwork)this.network, request.PubKey);

            if (request.CollateralAmount.ToDecimal(MoneyUnit.BTC) != collateralAmount)
            {
                this.logger.LogTrace("(-)[INVALID_COLLATERAL_REQUIREMENT]");
                context.State.Fail(MempoolErrors.InvalidCollateralRequirement, $"{context.Transaction.GetHash()} has a voting request with an invalid colateral requirement.").Throw();
            }

            // Prohibit re-use of collateral addresses.
            Script script = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(request.CollateralMainchainAddress);
            string collateralAddress = script.GetDestinationAddress(this.network).ToString();
            CollateralFederationMember owner = this.federationManager.CollateralAddressOwner(this.votingManager, VoteKey.AddFederationMember, collateralAddress);
            if (owner != null && owner.PubKey != request.PubKey)
            {
                this.logger.LogTrace("(-)[INVALID_COLLATERAL_REUSE]");
                context.State.Fail(MempoolErrors.VotingRequestInvalidCollateralReuse, $"{context.Transaction.GetHash()} has a voting request that's re-using collateral.").Throw();
            }
        }
    }
}