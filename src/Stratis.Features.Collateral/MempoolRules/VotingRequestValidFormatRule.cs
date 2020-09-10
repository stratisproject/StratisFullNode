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
    public class VotingRequestValidFormatRule : MempoolRule
    {
        private readonly JoinFederationRequestEncoder encoder;

        public VotingRequestValidFormatRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
            this.encoder = new JoinFederationRequestEncoder(loggerFactory);
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
        }
    }
}