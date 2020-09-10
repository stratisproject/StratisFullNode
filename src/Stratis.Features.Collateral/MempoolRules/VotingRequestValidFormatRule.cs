using System.Collections.Generic;
using System.Linq;
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
        private readonly VotingManager votingManager;

        public VotingRequestValidFormatRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory,
            VotingManager votingManager) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
            this.encoder = new JoinFederationRequestEncoder(loggerFactory);
            this.votingManager = votingManager;
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

            // Prohibit re-use of collateral addresses.
            Script script = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(request.CollateralMainchainAddress);
            string collateralAddress = script.GetDestinationAddress(this.network).ToString();
            CollateralFederationMember owner = CollateralAddressOwner(VoteKey.AddFederationMember, collateralAddress);
            if (owner != null && owner.PubKey != request.PubKey)
            {
                this.logger.LogTrace("(-)[INVALID_COLLATERAL_REUSE]");
                PoAConsensusErrors.VotingRequestInvalidCollateralReuse.Throw();
            }
        }

        private CollateralFederationMember GetMember(VotingData votingData)
        {
            if (!(this.network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory collateralPoAConsensusFactory))
                return null;

            if (!(collateralPoAConsensusFactory.DeserializeFederationMember(votingData.Data) is CollateralFederationMember collateralFederationMember))
                return null;

            return collateralFederationMember;
        }

        public CollateralFederationMember CollateralAddressOwner(VoteKey voteKey, string address)
        {
            List<Poll> finishedPolls = this.votingManager.GetFinishedPolls();

            CollateralFederationMember member = finishedPolls
                .Where(x => !x.IsExecuted && x.VotingData.Key == voteKey)
                .Select(x => this.GetMember(x.VotingData))
                .FirstOrDefault(x => x.CollateralMainchainAddress == address);
            
            if (member != null)
                return member;

            List<Poll> pendingPolls = this.votingManager.GetPendingPolls();

            member = pendingPolls
                .Where(x => x.VotingData.Key == voteKey)
                .Select(x => this.GetMember(x.VotingData))
                .FirstOrDefault(x => x.CollateralMainchainAddress == address);

            if (member != null)
                return member;

            List<VotingData> scheduledVotes = this.votingManager.GetScheduledVotes();

            member = scheduledVotes
                .Where(x => x.Key == voteKey)
                .Select(x => this.GetMember(x))
                .FirstOrDefault(x => x.CollateralMainchainAddress == address);

            return member;
        }
    }
}