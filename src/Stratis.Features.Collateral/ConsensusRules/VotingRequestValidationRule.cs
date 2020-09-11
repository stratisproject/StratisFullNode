using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.PoA.Features.Voting;
using Stratis.Features.Collateral;

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

            if (FederationVotingController.IsMultisigMember(this.network, request.PubKey))
            {
                this.logger.LogTrace("(-)[INVALID_MULTISIG_VOTING]");
                PoAConsensusErrors.VotingRequestInvalidMultisig.Throw();
            }

            // Check collateral amount?
            if (request.CollateralAmount.ToDecimal(MoneyUnit.BTC) != CollateralPoAMiner.MinerCollateralAmount)
            {
                this.logger.LogTrace("(-)[INVALID_COLLATERAL_REQUIREMENT]");
                PoAConsensusErrors.InvalidCollateralRequirement.Throw();
            }

            if (!(this.federationManager is CollateralFederationManager federationManager))
                return;

            // Prohibit re-use of collateral addresses.
            Script script = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(request.CollateralMainchainAddress);
            string collateralAddress = script.GetDestinationAddress(this.network).ToString();
            CollateralFederationMember owner = federationManager.CollateralAddressOwner(this.votingManager, VoteKey.AddFederationMember, collateralAddress);
            if (owner != null && owner.PubKey != request.PubKey)
            {
                this.logger.LogTrace("(-)[INVALID_COLLATERAL_REUSE]");
                PoAConsensusErrors.VotingRequestInvalidCollateralReuse.Throw();
            }
        }
    }
}