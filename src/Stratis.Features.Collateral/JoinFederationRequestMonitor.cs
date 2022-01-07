using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.PoA.Features.Voting;
using Stratis.Bitcoin.Signals;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.PoA.Voting;

namespace Stratis.Features.Collateral
{
    public class JoinFederationRequestMonitor
    {
        private readonly ILogger logger;
        private readonly ISignals signals;
        private readonly VotingManager votingManager;
        private readonly Network network;
        private readonly Network counterChainNetwork;
        private readonly IFederationManager federationManager;

        public JoinFederationRequestMonitor(VotingManager votingManager, Network network, CounterChainNetworkWrapper counterChainNetworkWrapper, IFederationManager federationManager, ISignals signals)
        {
            this.signals = signals;
            this.logger = LogManager.GetCurrentClassLogger();
            this.votingManager = votingManager;
            this.network = network;
            this.counterChainNetwork = counterChainNetworkWrapper.CounterChainNetwork;
            this.federationManager = federationManager;
        }

        public Task InitializeAsync()
        {
            this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);

            return Task.CompletedTask;
        }

        public static bool IsValid(JoinFederationRequest request, VotingManager votingManager, ILogger logger, Network network, Network counterChainNetwork)
        {
            // Skip if the member already exists.
            if (votingManager.IsFederationMember(request.PubKey))
                return false;


            // Check if the collateral amount is valid.
            decimal collateralAmount = request.CollateralAmount.ToDecimal(MoneyUnit.BTC);
            var expectedCollateralAmount = CollateralFederationMember.GetCollateralAmountForPubKey((PoANetwork)network, request.PubKey);

            if (collateralAmount != expectedCollateralAmount)
            {
                logger?.Debug("Ignoring voting collateral amount '{0}', when expecting '{1}'.", collateralAmount, expectedCollateralAmount);
                return false;
            }

            // Fill in the request.removalEventId (if any).
            byte[] federationMemberBytes = JoinFederationRequestService.GetFederationMemberBytes(request, network, counterChainNetwork);

            // Nothing to do if already voted.
            if (votingManager.AlreadyVotingFor(VoteKey.AddFederationMember, federationMemberBytes))
            {
                logger?.Debug("Skipping because already voted for adding '{0}'.", request.PubKey.ToHex());
                return false;
            }

            // Populate the RemovalEventId.
            JoinFederationRequestService.SetLastRemovalEventId(request, federationMemberBytes, votingManager);

            // Check the signature.
            PubKey key = PubKey.RecoverFromMessage(request.SignatureMessage, request.Signature);
            if (key.Hash != request.CollateralMainchainAddress)
            {
                logger?.Debug("Invalid collateral address validation signature.");
                return false;
            }

            return true;
        }

        public void OnBlockConnected(BlockConnected blockConnectedData)
        {
            if (!(this.network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory consensusFactory))
                return;

            // Only mining federation members vote to include new members.
            if (this.federationManager.CurrentFederationKey?.PubKey == null)
                return;

            List<IFederationMember> modifiedFederation = null;

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

                    // Only mining federation members vote to include new members.
                    modifiedFederation ??= this.votingManager.GetModifiedFederation(blockConnectedData.ConnectedBlock.ChainedHeader);
                    if (!modifiedFederation.Any(m => m.PubKey == this.federationManager.CurrentFederationKey.PubKey))
                    {
                        this.logger.Debug($"Ignoring as member '{this.federationManager.CurrentFederationKey.PubKey}' is not part of the federation at block '{blockConnectedData.ConnectedBlock.ChainedHeader}'.");
                        return;
                    }

                    if (!IsValid(request, this.votingManager, this.logger, this.network, this.counterChainNetwork))
                        continue;

                    // Vote to add the member.
                    this.logger.Debug("Voting to add federation member '{0}'.", request.PubKey.ToHex());

                    byte[] federationMemberBytes = JoinFederationRequestService.GetFederationMemberBytes(request, this.network, this.counterChainNetwork);

                    this.votingManager.ScheduleVote(new VotingData()
                    {
                        Key = VoteKey.AddFederationMember,
                        Data = federationMemberBytes
                    });

                }
                catch (Exception err)
                {
                    this.logger.Error(err.Message);
                }
            }
        }
    }
}
