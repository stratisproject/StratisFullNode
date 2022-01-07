using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.PoA.Features.Voting;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.PoA.Voting;
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
        private HashHeightPair lastCheckPoint;
        private IConsensusManager consensusManager;
        private Network counterChainNetwork;
        private IFullNode fullNode;

        public MandatoryCollateralMemberVotingRule(IFullNode fullNode, CounterChainNetworkWrapper counterChainNetworkWrapper) : base()
        {
            this.fullNode = fullNode;
            this.counterChainNetwork = counterChainNetworkWrapper.CounterChainNetwork;
        }

        [NoTrace]
        public override void Initialize()
        {
            this.consensusManager = this.fullNode.NodeService<IConsensusManager>();
            this.votingDataEncoder = new VotingDataEncoder();
            this.ruleEngine = (PoAConsensusRuleEngine)this.Parent;
            this.federationHistory = this.ruleEngine.FederationHistory;
            this.federationManager = this.ruleEngine.FederationManager;
            var lastCheckPoint = this.ruleEngine.Network.Checkpoints.LastOrDefault();
            this.lastCheckPoint = (lastCheckPoint.Value != null) ? new HashHeightPair(lastCheckPoint.Value.Hash, lastCheckPoint.Key) : null;

            base.Initialize();
        }

        private static bool HasRequest(Poll poll, Network network, IConsensusManager consensusManager, ChainIndexer chainIndexer)
        {
            ChainedHeader requestHeader = chainIndexer.GetHeader(poll.PollStartBlockData.Hash).Previous;
            ChainedHeaderBlock chb = consensusManager.GetBlockData(requestHeader.HashBlock);
            Block blockData = chb.Block;

            var encoder = new JoinFederationRequestEncoder();

            IFederationMember memberVotedOn = ((PoAConsensusFactory)(network.Consensus.ConsensusFactory)).DeserializeFederationMember(poll.VotingData.Data);

            Transaction joinTx = blockData.Transactions.FirstOrDefault(tx => JoinFederationRequestBuilder.Deconstruct(tx, encoder)?.PubKey == memberVotedOn.PubKey);

            return joinTx != null;
        }

        private static List<VotingData> NewPollsRequired(Network network, Network counterChainNetwork, IConsensusManager consensusManager, ChainedHeader contextHeader, VotingManager votingManager, NLog.ILogger logger)
        {
            ChainedHeader requestHeader = contextHeader.Previous;
            ChainedHeaderBlock chb = consensusManager.GetBlockData(requestHeader.HashBlock);
            Block blockData = chb.Block;

            var encoder = new JoinFederationRequestEncoder();

            return blockData.Transactions
                .Select(tx => JoinFederationRequestBuilder.Deconstruct(tx, encoder))
                .Where(jfr => jfr != null && JoinFederationRequestMonitor.IsValid(jfr, votingManager, logger, network, counterChainNetwork))
                .Select(jfr => new VotingData() { 
                    Key = VoteKey.AddFederationMember, 
                    Data = JoinFederationRequestService.GetFederationMemberBytes(jfr, network, counterChainNetwork) })
                .ToList();
        }

        public static List<VotingData> MinerAddMemberVotesExpected(PubKey blockMiner, ChainedHeader contextHeader, VotingManager votingManager, PoAConsensusOptions poaConsensusOptions, IConsensusManager consensusManager, ChainIndexer chainIndexer, Network network, Network counterChainNetwork)
        {
            bool IsTooOldToVoteOn(Poll poll) => poll.IsPending && (contextHeader.Height - poll.PollStartBlockData.Height) >= poaConsensusOptions.PollExpiryBlocks;

          IEnumerable<VotingData> res = votingManager.GetPendingPolls()
                .Where(p => p.VotingData.Key == VoteKey.AddFederationMember
                    && p.PollStartBlockData != null
                    && p.PollStartBlockData.Height <= contextHeader.Height
                    && !IsTooOldToVoteOn(p)
                    && !p.PubKeysHexVotedInFavor.Any(pk => pk.PubKey == blockMiner.ToHex())
                    && HasRequest(p, network, consensusManager, chainIndexer))
                .Select(p => p.VotingData)
                .Concat(NewPollsRequired(network, counterChainNetwork, consensusManager, contextHeader, votingManager, null));

            return res.ToList();
        }

        /// <summary>Checks that whomever mined this block is participating in any pending polls to vote-in new federation members.</summary>
        public override Task RunAsync(RuleContext context)
        {
            // Only start validating at the last checkpoint block.
            if (context.ValidationContext.ChainedHeaderToValidate.Height < (this.lastCheckPoint?.Height ?? 0))
                return Task.CompletedTask;

            var poaConsensusOptions = this.ruleEngine.ConsensusParams.Options as PoAConsensusOptions;

            PubKey blockMiner = this.federationHistory.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate).PubKey;

            List<VotingData> votesExpected = MinerAddMemberVotesExpected(blockMiner, context.ValidationContext.ChainedHeaderToValidate, this.ruleEngine.VotingManager, poaConsensusOptions, this.consensusManager, this.ruleEngine.ChainIndexer, this.ruleEngine.Network, this.counterChainNetwork);

            // Verify that the miner is including exactly the expected "add member" votes.
            Transaction coinbase = context.ValidationContext.BlockToValidate.Transactions[0];
            byte[] votingDataBytes = this.votingDataEncoder.ExtractRawVotingData(coinbase);

            if (votingDataBytes == null)
            {
                if (votesExpected.Any())
                {
                    this.Logger.LogWarning("The block at height {0} has no voting data but votes are expected.", context.ValidationContext.ChainedHeaderToValidate.Height);
                    // PoAConsensusErrors.BlockMissingVotes.Throw();
                }

                return Task.CompletedTask;
            }

            List<VotingData> votingDataList = this.votingDataEncoder.Decode(votingDataBytes);

            // Missing "add member" votes?
            if (votesExpected.Any(p => !votingDataList.Any(data => data == p)))
            {
                this.Logger.LogWarning("The block at height {0} has missing 'AddMember' votes.", context.ValidationContext.ChainedHeaderToValidate.Height);

                // TODO: Disabled temporarily.
                // PoAConsensusErrors.BlockMissingVotes.Throw();
            }

            // Unexpected "add member" votes?
            if (votingDataList.Any(data => data.Key == VoteKey.AddFederationMember && !votesExpected.Any(p => data == p)))
            {
                this.Logger.LogWarning("The block at height {0} has 'AddMember' votes that are unexpected.", context.ValidationContext.ChainedHeaderToValidate.Height);

                // TODO: Disabled temporarily.
                // PoAConsensusErrors.BlockUnexpectedVotes.Throw();
            }

            // Duplicate votes?
            if (votingDataList.Any(p => votingDataList.Count(data => data == p) != 1))
            {
                this.Logger.LogWarning("The block at height {0} has duplicate votes.", context.ValidationContext.ChainedHeaderToValidate.Height);

                // TODO: Disabled temporarily.
                // PoAConsensusErrors.BlockDuplicateVotes.Throw();
            }

            return Task.CompletedTask;
        }
    }
}