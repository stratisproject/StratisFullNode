using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.Collateral.ConsensusRules
{
    /// <summary>Used with the dynamic-mebership feature to validate <see cref="VotingData"/> 
    /// collection to ensure new members are being voted-in.</summary>
    public class MandatoryCollateralMemberVotingRule : FullValidationConsensusRule
    {
        private VotingDataEncoder votingDataEncoder;
        private PoAConsensusRuleEngine ruleEngine;
        private IFederationManager federationManager;
        private IFederationHistory federationHistory;

        [NoTrace]
        public override void Initialize()
        {
            this.votingDataEncoder = new VotingDataEncoder(this.Parent.LoggerFactory);
            this.ruleEngine = (PoAConsensusRuleEngine)this.Parent;
            this.federationManager = this.ruleEngine.FederationManager;
            this.federationHistory = this.ruleEngine.FederationHistory;

            base.Initialize();
        }

        /// <summary>Checks that whomever mined this block is participating in any pending polls to vote-in new federation members.</summary>
        public override Task RunAsync(RuleContext context)
        {
            // "AddFederationMember" polls, that were started at or before this height, that are still pending, which this node has voted in favor of.
            List<Poll> pendingPolls = this.ruleEngine.VotingManager.GetPendingPolls()
                .Where(p => p.VotingData.Key == VoteKey.AddFederationMember
                    && p.PollStartBlockData != null
                    && p.PollStartBlockData.Height <= context.ValidationContext.ChainedHeaderToValidate.Height
                    && p.PubKeysHexVotedInFavor.Any(pk => pk == this.federationManager.CurrentFederationKey.PubKey.ToHex())).ToList();

            // Exit if there aren't any.
            if (!pendingPolls.Any())
                return Task.CompletedTask;

            // Ignore any polls that the miner has already voted on.
            PubKey blockMiner = this.federationHistory.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate).PubKey;
            pendingPolls = pendingPolls.Where(p => !p.PubKeysHexVotedInFavor.Any(pk => pk == blockMiner.ToHex())).ToList();

            // Exit if there is nothing remaining.
            if (!pendingPolls.Any())
                return Task.CompletedTask;

            // Verify that the miner is including all the missing votes now.
            Transaction coinbase = context.ValidationContext.BlockToValidate.Transactions[0];
            byte[] votingDataBytes = this.votingDataEncoder.ExtractRawVotingData(coinbase);

            if (votingDataBytes == null)
                PoAConsensusErrors.BlockMissingVotes.Throw();

            // If any remaining polls are not found in the voting data list then throw a consenus error.
            List<VotingData> votingDataList = this.votingDataEncoder.Decode(votingDataBytes);
            if (pendingPolls.Any(p => !votingDataList.Any(data => data == p.VotingData)))
                PoAConsensusErrors.BlockMissingVotes.Throw();

            return Task.CompletedTask;
        }
    }
}