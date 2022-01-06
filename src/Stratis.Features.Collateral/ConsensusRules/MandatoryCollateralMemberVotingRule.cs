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
        private IFederationHistory federationHistory;
        private IFederationManager federationManager;

        [NoTrace]
        public override void Initialize()
        {
            this.votingDataEncoder = new VotingDataEncoder();
            this.ruleEngine = (PoAConsensusRuleEngine)this.Parent;
            this.federationHistory = this.ruleEngine.FederationHistory;
            this.federationManager = this.ruleEngine.FederationManager;

            base.Initialize();
        }

        public static List<VotingData> MinerAddMemberVotesExpected(PubKey blockMiner, int blockHeight, VotingManager votingManager, PoAConsensusOptions poaConsensusOptions)
        {
            bool IsTooOldToVoteOn(Poll poll) => poll.IsPending && (blockHeight - poll.PollStartBlockData.Height) >= poaConsensusOptions.PollExpiryBlocks;

            // It is assumed that all scheduled and non-expired pending "add member" polls are valid and should be voted on.
            // We rely on checks elsewhere to ensure that no invalid "add member" polls (not having a valid voting request) are created.
            IEnumerable<VotingData> res = votingManager.GetPendingPolls()
                .Where(p => p.VotingData.Key == VoteKey.AddFederationMember
                    && p.PollStartBlockData != null
                    && p.PollStartBlockData.Height <= blockHeight
                    && !IsTooOldToVoteOn(p)
                    && !p.PubKeysHexVotedInFavor.Any(pk => pk.PubKey == blockMiner.ToHex()))
                .Select(p => p.VotingData)
                .Concat(votingManager.GetScheduledVotes().Where(data => data.Key == VoteKey.AddFederationMember));

            return res.ToList();
        }

        /// <summary>Checks that whomever mined this block is participating in any pending polls to vote-in new federation members.</summary>
        public override Task RunAsync(RuleContext context)
        {
            // TODO: Disable in IBD?

            var poaConsensusOptions = this.ruleEngine.ConsensusParams.Options as PoAConsensusOptions;

            PubKey blockMiner = this.federationHistory.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate).PubKey;

            // Trust self.
            if (this.federationManager.CurrentFederationKey.PubKey == blockMiner)
                return Task.CompletedTask;

            List<VotingData> votesExpected = MinerAddMemberVotesExpected(blockMiner, context.ValidationContext.ChainedHeaderToValidate.Height, this.ruleEngine.VotingManager, poaConsensusOptions);

            // Verify that the miner is including exactly the expected "add member" votes.
            Transaction coinbase = context.ValidationContext.BlockToValidate.Transactions[0];
            byte[] votingDataBytes = this.votingDataEncoder.ExtractRawVotingData(coinbase);

            if (votingDataBytes == null)
            {
                if (votesExpected.Any())
                    PoAConsensusErrors.BlockMissingVotes.Throw();

                return Task.CompletedTask;
            }

            List<VotingData> votingDataList = this.votingDataEncoder.Decode(votingDataBytes);

            // Missing "add member" votes?
            if (votesExpected.Any(p => !votingDataList.Any(data => data == p)))
                PoAConsensusErrors.BlockMissingVotes.Throw();

            // Unexpected "add member" votes?
            if (votingDataList.Any(data => data.Key == VoteKey.AddFederationMember && !votesExpected.Any(p => data == p)))
                PoAConsensusErrors.BlockUnexpectedVotes.Throw();

            // Duplicate votes?
            if (votingDataList.Any(p => votingDataList.Count(data => data == p) != 1))
                PoAConsensusErrors.BlockDuplicateVotes.Throw();

            return Task.CompletedTask;
        }
    }
}