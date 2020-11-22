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
            // Determine the pending "AddFederationMember" polls that this node is participating in.
            List<Poll> pendingPolls = this.ruleEngine.VotingManager.GetPendingPolls()
                .Where(p => p.VotingData.Key == VoteKey.AddFederationMember
                    && p.PollStartBlockData != null
                    && p.PollStartBlockData.Height <= context.ValidationContext.ChainedHeaderToValidate.Height
                    && p.PubKeysHexVotedInFavor.Any(pk => pk == this.federationManager.CurrentFederationKey.PubKey.ToHex())).ToList();

            // If there is nothing to check then exit.
            if (!pendingPolls.Any())
                return Task.CompletedTask;

            // Ignore any polls that the miner has already voted on.
            PubKey blockMiner = this.slotsManager.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate, this.votingManager).PubKey;
            pendingPolls = pendingPolls.Where(p => !p.PubKeysHexVotedInFavor.Any(pk => pk == blockMiner.ToHex())).ToList();

            // If there is nothing remaining to check then exit.
            if (!pendingPolls.Any())
                return Task.CompletedTask;

            // Verify that the miner is including all the missing votes now.
            Transaction coinbase = context.ValidationContext.BlockToValidate.Transactions[0];
            byte[] votingDataBytes = this.votingDataEncoder.ExtractRawVotingData(coinbase);
            if (votingDataBytes == null)
                PoAConsensusErrors.BlockMissingVotes.Throw();

            List<VotingData> votingDataList = this.votingDataEncoder.Decode(votingDataBytes);
            if (pendingPolls.Any(p => !votingDataList.Any(data => pendingPolls.Any(p => p.VotingData == data))))
                PoAConsensusErrors.BlockMissingVotes.Throw();

            return Task.CompletedTask;
        }
    }
}