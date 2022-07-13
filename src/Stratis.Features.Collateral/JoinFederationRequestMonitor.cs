﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Configuration.Logging;
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
        private readonly IFederationManager federationManager;
        private readonly ILogger logger;
        private readonly ISignals signals;
        private readonly VotingManager votingManager;
        private readonly Network network;
        private readonly Network counterChainNetwork;
        private readonly NodeDeployments nodeDeployments;

        public JoinFederationRequestMonitor(VotingManager votingManager, Network network, CounterChainNetworkWrapper counterChainNetworkWrapper, ISignals signals, NodeDeployments nodeDeployments, IFederationManager federationManager)
        {
            this.signals = signals;
            this.logger = LogManager.GetCurrentClassLogger();
            this.votingManager = votingManager;
            this.network = network;
            this.counterChainNetwork = counterChainNetworkWrapper.CounterChainNetwork;
            this.nodeDeployments = nodeDeployments;
            this.federationManager = federationManager;

            this.signals.Subscribe<VotingManagerProcessBlock>(this.OnBlockConnected);
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public void OnBlockConnected(VotingManagerProcessBlock blockConnectedData)
        {
            if (!(this.network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory consensusFactory))
                return;

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

                    this.logger.LogDebug("Found federation join request at block {0}, transaction '{1}' for member '{2}'.", 
                        blockConnectedData.ConnectedBlock.ChainedHeader.Height, tx.GetHash(), request.PubKey.ToHex());

                    // Skip if the member already exists.
                    if (this.votingManager.IsMemberOfFederation(request.PubKey))
                    {
                        this.logger.LogDebug("Ignoring request because the member is already a member of the federation.");
                        continue;
                    }

                    // Check if the collateral amount is valid.
                    decimal collateralAmount = request.CollateralAmount.ToDecimal(MoneyUnit.BTC);
                    var expectedCollateralAmount = CollateralFederationMember.GetCollateralAmountForPubKey((PoANetwork)this.network, request.PubKey);

                    if (collateralAmount != expectedCollateralAmount)
                    {
                        this.logger.LogDebug("Ignoring voting collateral amount '{0}', when expecting '{1}'.", collateralAmount, expectedCollateralAmount);

                        continue;
                    }

                    // Fill in the request.removalEventId (if any).
                    byte[] federationMemberBytes = JoinFederationRequestService.GetFederationMemberBytes(request, this.network, this.counterChainNetwork);

                    // Nothing to do if already voted. Ignore scheduled polls as we could be in IBD.
                    if (this.votingManager.AlreadyVotingFor(VoteKey.AddFederationMember, federationMemberBytes, false))
                    {
                        this.logger.LogDebug("Skipping because already voted for adding '{0}'.", request.PubKey.ToHex());
                        continue;
                    }

                    // Populate the RemovalEventId.
                    JoinFederationRequestService.SetLastRemovalEventId(request, federationMemberBytes, this.votingManager);

                    // Check the signature.
                    PubKey key = PubKey.RecoverFromMessage(request.SignatureMessage, request.Signature);
                    if (key.Hash != request.CollateralMainchainAddress)
                    {
                        this.logger.LogDebug("Invalid collateral address validation signature for joining federation via transaction '{0}'", tx.GetHash());
                        continue;
                    }

                    // Vote to add the member.
                    this.logger.LogDebug("Voting to add federation member '{0}'.", request.PubKey.ToHex());

                    VotingData votingData = new VotingData()
                    {
                        Key = VoteKey.AddFederationMember,
                        Data = federationMemberBytes
                    };

                    if (blockConnectedData.ConnectedBlock.ChainedHeader.Height >= ((PoAConsensusOptions)this.network.Consensus.Options).Release1300ActivationHeight)
                    {
                        // If we are executing this from the miner, there will be no transaction present.
                        if (blockConnectedData.PollsRepositoryTransaction == null)
                        {
                            // Create a pending poll so that the scheduled vote is not "sanitized" away.
                            this.votingManager.PollsRepository.WithTransaction(transaction =>
                            {
                                this.votingManager.CreatePendingPoll(transaction, votingData, blockConnectedData.ConnectedBlock.ChainedHeader);
                                transaction.Commit();
                            });
                        }
                        else
                        {
                            this.votingManager.CreatePendingPoll(blockConnectedData.PollsRepositoryTransaction, votingData, blockConnectedData.ConnectedBlock.ChainedHeader);
                        }
                    }

                    // If this node is a federation member then schedule a vote.
                    if (this.federationManager.IsFederationMember)
                        this.votingManager.ScheduleVote(votingData);
                }
                catch (Exception err)
                {
                    this.logger.LogError(err.Message);
                }
            }
        }
    }
}
