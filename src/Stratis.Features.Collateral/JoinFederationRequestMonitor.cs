using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
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
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger logger;
        private readonly ISignals signals;
        private SubscriptionToken blockConnectedToken;
        private readonly VotingManager votingManager;
        private readonly IFederationManager federationManager;
        private readonly Network network;
        private readonly Network counterChainNetwork;

        public JoinFederationRequestMonitor(VotingManager votingManager, IFederationManager federationManager, Network network, CounterChainNetworkWrapper counterChainNetworkWrapper, ISignals signals, ILoggerFactory loggerFactory)
        {
            this.signals = signals;
            this.loggerFactory = loggerFactory;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.votingManager = votingManager;
            this.federationManager = federationManager;
            this.network = network;
            this.counterChainNetwork = counterChainNetworkWrapper.CounterChainNetwork;
        }

        public Task InitializeAsync()
        {
            this.blockConnectedToken = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);

            return Task.CompletedTask;
        }

        private void OnBlockConnected(BlockConnected blockConnectedData)
        {
            List<Transaction> transactions = blockConnectedData.ConnectedBlock.Block.Transactions;

            var encoder = new JoinFederationRequestEncoder(this.loggerFactory);
            List<Poll> finishedPolls = null;
            List<Poll> pendingPolls = null;

            if (!(this.network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory consensusFactory))
                return;

            for (int i = 0; i < transactions.Count; i++)
            {
                Transaction tx = transactions[i];

                try
                {
                    JoinFederationRequest request = JoinFederationRequestBuilder.Deconstruct(tx, encoder);

                    if (request == null)
                        continue;

                    // TODO: Skip if the member already exists.

                    // Check if the collateral amount is valid.
                    decimal collateralAmount = request.CollateralAmount.ToDecimal(MoneyUnit.BTC);
                    if (collateralAmount != 10_000m && collateralAmount != 50_000m)
                    {
                        this.logger.LogDebug("Ignoring voting request with invalid collateral amount '{0}'.", collateralAmount);

                        continue;
                    }

                    // Fill in the request.removalEventId (if any).
                    Script collateralScript = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(request.CollateralMainchainAddress);
                    
                    // TODO: Perhaps there is a better way than this.
                    bool isMultisigMember = collateralAmount == 50_000m;

                    var collateralFederationMember = new CollateralFederationMember(request.PubKey, isMultisigMember, request.CollateralAmount, collateralScript.GetDestinationAddress(this.counterChainNetwork).ToString());

                    byte[] federationMemberBytes = consensusFactory.SerializeFederationMember(collateralFederationMember);

                    // Nothing to do if already voted.
                    if (this.votingManager.AlreadyVotingFor(VoteKey.AddFederationMember, federationMemberBytes))
                    {
                        this.logger.LogDebug("Skipping because already voted for adding '{0}'.", request.PubKey.ToHex());

                        continue;
                    }

                    finishedPolls = finishedPolls ?? this.votingManager.GetFinishedPolls();

                    // Populate the RemovalEventId.
                    Poll poll = finishedPolls.FirstOrDefault(x => !x.IsExecuted &&
                          x.VotingData.Key == VoteKey.KickFederationMember && x.VotingData.Data.SequenceEqual(federationMemberBytes));

                    request.RemovalEventId = (poll == null) ? Guid.Empty : new Guid(poll.PollExecutedBlockData.ToBytes());

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
                    this.logger.LogDebug(err.Message);
                }
            }
        }
    }
}
