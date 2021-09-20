using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Rules;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Features.Collateral.ConsensusRules;

namespace Stratis.Bitcoin.Features.Collateral.MempoolRules
{
    public class VotingRequestValidationRule : MempoolRule
    {
        private readonly VotingRequestFullValidationRule votingRequestFullValidationRule;

        public VotingRequestValidationRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory,
            IEnumerable<IFullValidationConsensusRule> rules) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
            this.votingRequestFullValidationRule = rules.OfType<VotingRequestFullValidationRule>().First();
        }

        /// <inheritdoc/>
        public override void CheckTransaction(MempoolValidationContext context)
        {
            try
            {
                this.votingRequestFullValidationRule.CheckTransaction(context.Transaction, this.chainIndexer.Height);
            }
            catch (ConsensusErrorException err) when (err.ConsensusError == PoAConsensusErrors.InvalidVotingOnMultiSig)
            {
                context.State.Fail(MempoolErrors.VotingRequestInvalidMultisig, $"{context.Transaction.GetHash()} has an invalid voting request for a multisig member.").Throw();
            }
            catch (ConsensusErrorException err) when (err.ConsensusError == PoAConsensusErrors.InvalidCollateralRequirement)
            {
                context.State.Fail(MempoolErrors.InvalidCollateralRequirement, $"{context.Transaction.GetHash()} has a voting request with an invalid colateral requirement.").Throw();
            }
            catch (ConsensusErrorException err) when (err.ConsensusError == PoAConsensusErrors.VotingRequestInvalidCollateralReuse)
            {
                context.State.Fail(MempoolErrors.VotingRequestInvalidCollateralReuse, $"{context.Transaction.GetHash()} has a voting request that's re-using collateral.").Throw();
            }
        }
    }
}