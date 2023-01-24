﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Validators;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Mining;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.PoA.Collateral;
using Stratis.Features.PoA.Collateral.CounterChain;

namespace Stratis.Features.Collateral
{
    /// <summary>
    /// Collateral aware version of <see cref="PoAMiner"/>. At the block template creation it will check our own collateral at a commitment height which is
    /// calculated in a following way: <c>counter chain height - maxReorgLength - AddressIndexer.SyncBuffer</c>. Then commitment height is encoded in
    /// OP_RETURN output of a coinbase transaction.
    /// </summary>
    public class CollateralPoAMiner : PoAMiner
    {
        private readonly CollateralHeightCommitmentEncoder encoder;

        private readonly ICollateralChecker collateralChecker;

        private readonly Network counterChainNetwork;

        private readonly ChainIndexer chainIndexer;

        private readonly JoinFederationRequestMonitor joinFederationRequestMonitor;

        private readonly CancellationTokenSource cancellationSource;

        public CollateralPoAMiner(IConsensusManager consensusManager, IDateTimeProvider dateTimeProvider, Network network, INodeLifetime nodeLifetime, IInitialBlockDownloadState ibdState,
            BlockDefinition blockDefinition, ISlotsManager slotsManager, IConnectionManager connectionManager, JoinFederationRequestMonitor joinFederationRequestMonitor, PoABlockHeaderValidator poaHeaderValidator,
            IFederationManager federationManager, IFederationHistory federationHistory, IIntegrityValidator integrityValidator, IWalletManager walletManager, ChainIndexer chainIndexer, INodeStats nodeStats,
            VotingManager votingManager, PoASettings poAMinerSettings, ICollateralChecker collateralChecker, IAsyncProvider asyncProvider, ICounterChainSettings counterChainSettings, IIdleFederationMembersKicker idleFederationMembersKicker,
            NodeSettings nodeSettings)
            : base(consensusManager, dateTimeProvider, network, nodeLifetime, ibdState, blockDefinition, slotsManager, connectionManager, poaHeaderValidator,
            federationManager, federationHistory, integrityValidator, walletManager, nodeStats, votingManager, poAMinerSettings, asyncProvider, idleFederationMembersKicker, nodeSettings)
        {
            this.counterChainNetwork = counterChainSettings.CounterChainNetwork;
            this.collateralChecker = collateralChecker;
            this.encoder = new CollateralHeightCommitmentEncoder();
            this.chainIndexer = chainIndexer;
            this.joinFederationRequestMonitor = joinFederationRequestMonitor;
            this.cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(nodeLifetime.ApplicationStopping);
        }

        /// <inheritdoc />
        protected override void FillBlockTemplate(BlockTemplate blockTemplate, out bool dropTemplate)
        {
            OnBeforeFillBlockTemplate();

            base.FillBlockTemplate(blockTemplate, out dropTemplate);

            int counterChainHeight = this.collateralChecker.GetCounterChainConsensusHeight();
            int maxReorgLength = AddressIndexer.GetMaxReorgOrFallbackMaxReorg(this.network);

            int commitmentHeight = counterChainHeight - maxReorgLength - AddressIndexer.SyncBuffer;

            if (commitmentHeight <= 0)
            {
                dropTemplate = true;
                if (counterChainHeight != 0)
                    this.logger.LogWarning("Counter chain should first advance at least at {0}! Block can't be produced.", maxReorgLength + AddressIndexer.SyncBuffer);

                this.logger.LogTrace("(-)[LOW_COMMITMENT_HEIGHT]");
                return;
            }

            // Check that the commitment height is not less that of the prior block.
            ChainedHeaderBlock prevBlock = this.consensusManager.GetBlockData(blockTemplate.Block.Header.HashPrevBlock);
            (int? commitmentHeightPrev, _) = this.encoder.DecodeCommitmentHeight(prevBlock.Block.Transactions.First());
            // If the intended commitment height is less than the previous block's commitment height, update our local
            // counter chain height and try again.
            if (commitmentHeight < commitmentHeightPrev)
            {
                this.collateralChecker.UpdateCollateralInfoAsync(this.cancellationSource.Token).GetAwaiter().GetResult();
                counterChainHeight = this.collateralChecker.GetCounterChainConsensusHeight();
                commitmentHeight = counterChainHeight - maxReorgLength - AddressIndexer.SyncBuffer;

                if (commitmentHeight < commitmentHeightPrev)
                {
                    dropTemplate = true;
                    this.logger.LogWarning("Block can't be produced, the counter chain should first advance at least {0} blocks. ", commitmentHeightPrev - commitmentHeight);
                    this.logger.LogTrace("(-)[LOW_COMMITMENT_HEIGHT]");
                    return;
                }
            }

            IFederationMember currentMember = this.federationManager.GetCurrentFederationMember();

            if (currentMember == null)
            {
                dropTemplate = true;
                this.logger.LogWarning("Unable to get this node's federation member!");
                this.logger.LogTrace("(-)[CANT_GET_FED_MEMBER]");
                return;
            }

            // Check our own collateral at a given commitment height.
            bool success = this.collateralChecker.CheckCollateral(currentMember, commitmentHeight, this.chainIndexer[blockTemplate.Block.Header.HashPrevBlock].Height + 1);

            if (!success)
            {
                dropTemplate = true;
                this.logger.LogWarning("Failed to fulfill collateral requirement for mining!");
                this.logger.LogTrace("(-)[BAD_COLLATERAL]");
                return;
            }

            // Add height commitment.
            byte[] encodedHeight = this.encoder.EncodeCommitmentHeight(commitmentHeight);

            var heightCommitmentScript = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(encodedHeight), Op.GetPushOp(this.counterChainNetwork.MagicBytes));
            blockTemplate.Block.Transactions[0].AddOutput(Money.Zero, heightCommitmentScript);
        }


        /// <summary>
        /// It is possible that this node was not a federation member at the time a pending poll was started.
        /// As such the node would not have voted up to now. We have to check if a vote should be added now.
        /// </summary>
        /// <remarks>
        /// There is another scenario catered for by this method. It's the situation where a node crashed or is
        /// stopped when it contains scheduled "add member" votes that have not yet been added to a block.
        /// </remarks>
        private void OnBeforeFillBlockTemplate()
        {
            if (!this.federationManager.IsFederationMember || !this.network.ConsensusOptions.VotingEnabled)
                return;

            try
            {
                List<Poll> pendingAddFederationMemberPolls = this.votingManager.GetPendingPolls().Where(p => p.VotingData.Key == VoteKey.AddFederationMember).ToList();

                // Filter all polls where this federation number has not voted on.
                pendingAddFederationMemberPolls = pendingAddFederationMemberPolls
                    .Where(p => !p.PubKeysHexVotedInFavor.Any(v => v.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToString()))
                    .Where(p => !this.votingManager.GetScheduledVotes().Any(v => v == p.VotingData))
                    .ToList();

                if (!pendingAddFederationMemberPolls.Any())
                {
                    this.logger.LogDebug("There are no outstanding add member polls for this node to vote on.");
                    return;
                }

                foreach (Poll poll in pendingAddFederationMemberPolls)
                {
                    this.logger.LogDebug($"Attempting to cast outstanding vote on poll '{poll.Id}'.");

                    this.votingManager.ScheduleVote(poll.VotingData);
                }

                return;
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return;
            }
        }
    }
}
