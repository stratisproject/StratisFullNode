using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.PoA.Features.Voting;
using Stratis.Bitcoin.Signals;
using Stratis.Features.Collateral.CounterChain;

namespace Stratis.Features.Collateral
{
    public class JoinFederationRequestMonitor
    {
        private readonly ILogger logger;
        private readonly ISignals signals;
        private SubscriptionToken blockConnectedToken;
        private readonly VotingManager votingManager;
        private readonly Network network;
        private readonly Network counterChainNetwork;
        private readonly IFederationManager federationManager;
        private readonly HashSet<uint256> pollsCheckedWithJoinFederationRequestMonitor;

        public JoinFederationRequestMonitor(VotingManager votingManager, Network network, CounterChainNetworkWrapper counterChainNetworkWrapper, IFederationManager federationManager, ISignals signals)
        {
            this.signals = signals;
            this.logger = LogManager.GetCurrentClassLogger();
            this.votingManager = votingManager;
            this.network = network;
            this.counterChainNetwork = counterChainNetworkWrapper.CounterChainNetwork;
            this.federationManager = federationManager;
            this.pollsCheckedWithJoinFederationRequestMonitor = new HashSet<uint256>();
        }

        public bool AlreadyChecked(uint256 hash) => this.pollsCheckedWithJoinFederationRequestMonitor.Contains(hash);

        public Task InitializeAsync()
        {
            this.blockConnectedToken = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);

            return Task.CompletedTask;
        }

        public void OnBlockConnected(BlockConnected blockConnectedData)
        {
            if (!(this.network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory consensusFactory))
                return;

            // Only mining federation members vote to include new members.
            if (this.federationManager.CurrentFederationKey?.PubKey == null)
                return;

            List<IFederationMember> modifiedFederation = null;

            this.pollsCheckedWithJoinFederationRequestMonitor.Add(blockConnectedData.ConnectedBlock.ChainedHeader.HashBlock);

            List<Transaction> transactions = blockConnectedData.ConnectedBlock.Block.Transactions;

            var encoder = new JoinFederationRequestEncoder();

            for (int i = 0; i < transactions.Count; i++)
            {
                Transaction tx = transactions[i];

                try
                {
                    JoinFederationRequest request = JoinFederationRequestBuilder.Deconstruct(tx, encoder);

                    if (request == null)
                        continue;

                    // Skip if the member already exists.
                    if (this.votingManager.IsFederationMember(request.PubKey))
                        continue;

                    // Only mining federation members vote to include new members.
                    modifiedFederation = modifiedFederation ?? this.votingManager.GetModifiedFederation(blockConnectedData.ConnectedBlock.ChainedHeader);
                    if (!modifiedFederation.Any(m => m.PubKey == this.federationManager.CurrentFederationKey.PubKey))
                        return;

                    // Check if the collateral amount is valid.
                    decimal collateralAmount = request.CollateralAmount.ToDecimal(MoneyUnit.BTC);
                    var expectedCollateralAmount = CollateralFederationMember.GetCollateralAmountForPubKey((PoANetwork)this.network, request.PubKey);

                    if (collateralAmount != expectedCollateralAmount)
                    {
                        this.logger.LogDebug("Ignoring voting collateral amount '{0}', when expecting '{1}'.", collateralAmount, expectedCollateralAmount);

                        continue;
                    }

                    // Fill in the request.removalEventId (if any).
                    Script collateralScript = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(request.CollateralMainchainAddress);

                    var collateralFederationMember = new CollateralFederationMember(request.PubKey, false, request.CollateralAmount, collateralScript.GetDestinationAddress(this.counterChainNetwork).ToString());

                    byte[] federationMemberBytes = consensusFactory.SerializeFederationMember(collateralFederationMember);

                    // Nothing to do if already voted.
                    if (this.votingManager.AlreadyVotingFor(VoteKey.AddFederationMember, federationMemberBytes))
                    {
                        this.logger.LogDebug("Skipping because already voted for adding '{0}'.", request.PubKey.ToHex());

                        continue;
                    }

                    // Populate the RemovalEventId.
                    Poll poll = this.votingManager.GetApprovedPolls().FirstOrDefault(x => x.IsExecuted &&
                          x.VotingData.Key == VoteKey.KickFederationMember && x.VotingData.Data.SequenceEqual(federationMemberBytes));

                    request.RemovalEventId = (poll == null) ? Guid.Empty : new Guid(poll.PollExecutedBlockData.Hash.ToBytes().TakeLast(16).ToArray());

                    // Check the signature.
                    PubKey key = PubKey.RecoverFromMessage(request.SignatureMessage, request.Signature);
                    if (key.Hash != request.CollateralMainchainAddress)
                    {
                        this.logger.LogDebug("Invalid collateral address validation signature for joining federation via transaction '{0}'", tx.GetHash());
                        continue;
                    }

                    // Vote to add the member.
                    this.logger.LogDebug("Voting to add federation member '{0}'.", request.PubKey.ToHex());

                    this.votingManager.ScheduleVote(new VotingData()
                    {
                        Key = VoteKey.AddFederationMember,
                        Data = federationMemberBytes
                    });

                }
                catch (Exception err)
                {
                    this.logger.LogError(err.Message);
                }
            }
        }
    }
}
