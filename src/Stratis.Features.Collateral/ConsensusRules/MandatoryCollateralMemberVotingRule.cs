using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Features.Collateral;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.Collateral.ConsensusRules
{
    /// <summary>Used with the dynamic-mebership feature to validate <see cref="VotingData"/> 
    /// collection to ensure new members are being voted-in.</summary>
    public class MandatoryCollateralMemberVotingRule : PartialValidationConsensusRule
    {
        private VotingDataEncoder votingDataEncoder;
        private PoAConsensusRuleEngine ruleEngine;
        private Network network;
        private IFederationManager federationManager;
        private ISlotsManager slotsManager;
        private CollateralPoAConsensusFactory consensusFactory;
        private IChainState chainState;
        private ILogger logger;

        [NoTrace]
        public override void Initialize()
        {
            this.votingDataEncoder = new VotingDataEncoder(this.Parent.LoggerFactory);
            this.ruleEngine = (PoAConsensusRuleEngine)this.Parent;
            this.logger = this.Parent.LoggerFactory.CreateLogger(this.GetType().FullName);
            this.network = this.Parent.Network;
            this.federationManager = this.ruleEngine.FederationManager;
            this.slotsManager = this.ruleEngine.SlotsManager;
            this.consensusFactory = (CollateralPoAConsensusFactory)this.network.Consensus.ConsensusFactory;
            this.chainState = this.ruleEngine.ChainState;

            base.Initialize();
        }

        /// <summary>Checks that whomever mined this block is participating in any pending polls to vote-in new federation members.</summary>
        public override Task RunAsync(RuleContext context)
        {
            List<Poll> pendingPolls = this.ruleEngine.VotingManager.GetPendingPolls();
            if (!pendingPolls.Any())
                return Task.CompletedTask;

            // Determine who mined the block.
            PubKey blockMiner = this.GetBlockMiner(context.ValidationContext.ChainedHeaderToValidate);

            // Check whether whomever mined the block has any outstanding votes for adding new federation members.
            List<CollateralFederationMember> expectedVotes = pendingPolls
                .Where(p => p.VotingData.Key == VoteKey.AddFederationMember && !p.PubKeysHexVotedInFavor.Any(pk => pk == blockMiner.ToHex()))
                .Select(p => (CollateralFederationMember)this.consensusFactory.DeserializeFederationMember(p.VotingData.Data)).ToList();

            // If this miner has no outstanding expected votes, then return.
            if (!expectedVotes.Any())
                return Task.CompletedTask;

            // Otherwise check that the miner is including those votes now.
            Transaction coinbase = context.ValidationContext.BlockToValidate.Transactions[0];

            Dictionary<string, bool> checkList = expectedVotes.ToDictionary(x => x.PubKey.ToHex(), x => false);

            byte[] votingDataBytes = this.votingDataEncoder.ExtractRawVotingData(coinbase);
            if (votingDataBytes != null)
            {
                List<VotingData> votingDataList = this.votingDataEncoder.Decode(votingDataBytes);
                foreach (VotingData votingData in votingDataList)
                {
                    var member = (CollateralFederationMember)this.consensusFactory.DeserializeFederationMember(votingData.Data);
                    
                    // Check collateral amount.
                    if (member.CollateralAmount.ToDecimal(MoneyUnit.BTC) != CollateralPoAMiner.MinerCollateralAmount)
                    {
                        this.logger.LogTrace("(-)[INVALID_COLLATERAL_REQUIREMENT]");
                        PoAConsensusErrors.InvalidCollateralRequirement.Throw();
                    }

                    // Can't be a multisig member.
                    if (member.IsMultisigMember)
                    {
                        this.logger.LogTrace("(-)[INVALID_MULTISIG_VOTING]");
                        PoAConsensusErrors.VotingRequestInvalidMultisig.Throw();
                    }

                    checkList[member.PubKey.ToHex()] = true;
                }
            }

            // If any outstanding votes have not been included throw a consensus error.
            if (checkList.Any(c => !c.Value))
                PoAConsensusErrors.BlockMissingVotes.Throw();

            return Task.CompletedTask;
        }

        public PubKey GetBlockMiner(ChainedHeader currentHeader)
        {
            List<IFederationMember> modifiedFederation = this.federationManager.GetFederationMembers();
            var consensusFactory = (CollateralPoAConsensusFactory)this.network.Consensus.ConsensusFactory;

            foreach (Poll poll in this.ruleEngine.VotingManager.GetFinishedPolls().Where(x => !x.IsExecuted &&
                ((x.VotingData.Key == VoteKey.AddFederationMember) || (x.VotingData.Key == VoteKey.KickFederationMember))))
            {
                if ((currentHeader.Height - poll.PollVotedInFavorBlockData.Height) <= this.network.Consensus.MaxReorgLength)
                    // Not applied yet.
                    continue;

                IFederationMember federationMember = consensusFactory.DeserializeFederationMember(poll.VotingData.Data);

                if (poll.VotingData.Key == VoteKey.AddFederationMember)
                    modifiedFederation.Add(federationMember);
                else if (poll.VotingData.Key == VoteKey.KickFederationMember)
                    modifiedFederation.Remove(federationMember);
            }

            return this.slotsManager.GetFederationMemberForTimestamp(currentHeader.Header.Time, modifiedFederation).PubKey;
        }
    }
}
