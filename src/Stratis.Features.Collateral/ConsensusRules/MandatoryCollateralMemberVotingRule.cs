using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        private Network network;
        private IFederationManager federationManager;
        private VotingManager votingManager;
        private ISlotsManager slotsManager;
        private CollateralPoAConsensusFactory consensusFactory;
        private ILoggerFactory loggerFactory;
        private ILogger logger;

        [NoTrace]
        public override void Initialize()
        {
            this.votingDataEncoder = new VotingDataEncoder(this.Parent.LoggerFactory);
            this.ruleEngine = (PoAConsensusRuleEngine)this.Parent;
            this.loggerFactory = this.Parent.LoggerFactory;
            this.logger = this.loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = this.Parent.Network;
            this.federationManager = this.ruleEngine.FederationManager;
            this.votingManager = this.ruleEngine.VotingManager;
            this.slotsManager = this.ruleEngine.SlotsManager;
            this.consensusFactory = (CollateralPoAConsensusFactory)this.network.Consensus.ConsensusFactory;

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
                    && p.PubKeysHexVotedInFavor.Any(pk => pk == "03a620f0ba4f197b53ba3e8591126b54bd728ecc961607221190abb8e3cd91ea5f")).ToList();

            // Exit if there aren't any.
            if (!pendingPolls.Any())
                return Task.CompletedTask;

            // Ignore any polls that the miner has already voted on.
            PubKey blockMiner = this.slotsManager.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate, this.votingManager).PubKey;
            pendingPolls = pendingPolls.Where(p => !p.PubKeysHexVotedInFavor.Any(pk => pk == blockMiner.ToHex())).ToList();

            // Exit if there is nothing remaining.
            if (!pendingPolls.Any())
                return Task.CompletedTask;

            // Verify that the miner is including all the missing votes now.
            Transaction coinbase = context.ValidationContext.BlockToValidate.Transactions[0];
            byte[] votingDataBytes = this.votingDataEncoder.ExtractRawVotingData(coinbase);

            // If there are no voting data then just return, we could be dealing with pending polls
            // that were never executed (picked up) by other nodes.
            if (votingDataBytes == null)
                return Task.CompletedTask;

            // If any remaining polls are not found in the voting data list then throw a consenus error.
            List<VotingData> votingDataList = this.votingDataEncoder.Decode(votingDataBytes);
            if (pendingPolls.Any(p => !votingDataList.Any(data => data == p.VotingData)))
                PoAConsensusErrors.BlockMissingVotes.Throw();

            return Task.CompletedTask;
        }
    }
}