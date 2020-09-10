using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.PoA.Features.Voting;

namespace Stratis.Bitcoin.Features.PoA.Voting.MempoolRules
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
            
            // TODO: Check collateral amount?
        }
    }
}