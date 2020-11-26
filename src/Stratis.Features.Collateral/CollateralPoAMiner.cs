using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
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
using Stratis.Features.Collateral.CounterChain;

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

        public CollateralPoAMiner(IConsensusManager consensusManager, IDateTimeProvider dateTimeProvider, Network network, INodeLifetime nodeLifetime, ILoggerFactory loggerFactory,
            IInitialBlockDownloadState ibdState, BlockDefinition blockDefinition, ISlotsManager slotsManager, IConnectionManager connectionManager, JoinFederationRequestMonitor joinFederationRequestMonitor,
            PoABlockHeaderValidator poaHeaderValidator, IFederationManager federationManager, IIntegrityValidator integrityValidator, IWalletManager walletManager, ChainIndexer chainIndexer,
            INodeStats nodeStats, VotingManager votingManager, PoAMinerSettings poAMinerSettings, ICollateralChecker collateralChecker, IAsyncProvider asyncProvider, ICounterChainSettings counterChainSettings, IIdleFederationMembersKicker idleFederationMembersKicker)
            : base(consensusManager, dateTimeProvider, network, nodeLifetime, loggerFactory, ibdState, blockDefinition, slotsManager, connectionManager,
            poaHeaderValidator, federationManager, integrityValidator, walletManager, nodeStats, votingManager, poAMinerSettings, asyncProvider, idleFederationMembersKicker)
        {
            this.counterChainNetwork = counterChainSettings.CounterChainNetwork;
            this.collateralChecker = collateralChecker;
            this.encoder = new CollateralHeightCommitmentEncoder(this.logger);
            this.chainIndexer = chainIndexer;
            this.joinFederationRequestMonitor = joinFederationRequestMonitor;
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
                this.logger.LogWarning("Counter chain should first advance at least at {0}! Block can't be produced.", maxReorgLength + AddressIndexer.SyncBuffer);
                this.logger.LogTrace("(-)[LOW_COMMITMENT_HEIGHT]");
                return;
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
            bool success = this.collateralChecker.CheckCollateral(currentMember, commitmentHeight);

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
                pendingAddFederationMemberPolls = pendingAddFederationMemberPolls.Where(p => !p.PubKeysHexVotedInFavor.Contains(this.federationManager.CurrentFederationKey.PubKey.ToString())).ToList();

                if (!pendingAddFederationMemberPolls.Any())
                    return;

                IFederationMember collateralFederationMember = this.federationManager.GetCurrentFederationMember();

                var poaConsensusFactory = this.network.Consensus.ConsensusFactory as PoAConsensusFactory;

                foreach (Poll poll in pendingAddFederationMemberPolls)
                {
                    ChainedHeader pollStartHeader = this.chainIndexer.GetHeader(poll.PollStartBlockData.Hash);
                    ChainedHeader votingRequestHeader = pollStartHeader.Previous;

                    // Already checked?
                    if (this.joinFederationRequestMonitor.AlreadyChecked(votingRequestHeader.HashBlock))
                        continue;

                    var blockData = this.consensusManager.GetBlockData(votingRequestHeader.HashBlock);

                    this.joinFederationRequestMonitor.OnBlockConnected(new Bitcoin.EventBus.CoreEvents.BlockConnected(
                        new ChainedHeaderBlock(blockData.Block, votingRequestHeader)));
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

    public sealed class CollateralHeightCommitmentEncoder
    {
        /// <summary>Prefix used to identify OP_RETURN output with mainchain consensus height commitment.</summary>
        public static readonly byte[] HeightCommitmentOutputPrefixBytes = { 121, 13, 6, 253 };

        private readonly ILogger logger;

        public CollateralHeightCommitmentEncoder(ILogger logger)
        {
            this.logger = logger;
        }

        /// <summary>Converts <paramref name="height"/> to a byte array which has a prefix of <see cref="HeightCommitmentOutputPrefixBytes"/>.</summary>
        /// <param name="height">That height at which the block was mined.</param>
        /// <returns>The encoded height in bytes.</returns>
        public byte[] EncodeCommitmentHeight(int height)
        {
            var bytes = new List<byte>(HeightCommitmentOutputPrefixBytes);

            bytes.AddRange(BitConverter.GetBytes(height));

            return bytes.ToArray();
        }

        /// <summary>Extracts the height commitment data from a transaction's coinbase <see cref="TxOut"/>.</summary>
        /// <param name="coinbaseTx">The transaction that should contain the height commitment data.</param>
        /// <returns>The commitment height, <c>null</c> if not found.</returns>
        public (int? height, uint? magic) DecodeCommitmentHeight(Transaction coinbaseTx)
        {
            IEnumerable<Script> opReturnOutputs = coinbaseTx.Outputs.Where(x => (x.ScriptPubKey.Length > 0) && (x.ScriptPubKey.ToBytes(true)[0] == (byte)OpcodeType.OP_RETURN)).Select(x => x.ScriptPubKey);

            byte[] commitmentData = null;
            byte[] magic = null;

            this.logger.LogDebug("Transaction contains {0} OP_RETURN outputs.", opReturnOutputs.Count());

            foreach (Script script in opReturnOutputs)
            {
                Op[] ops = script.ToOps().ToArray();

                if (ops.Length != 2 && ops.Length != 3)
                    continue;

                byte[] data = ops[1].PushData;

                bool correctPrefix = data.Take(HeightCommitmentOutputPrefixBytes.Length).SequenceEqual(HeightCommitmentOutputPrefixBytes);

                if (!correctPrefix)
                {
                    this.logger.LogDebug("Push data contains incorrect prefix for height commitment.");
                    continue;
                }

                commitmentData = data.Skip(HeightCommitmentOutputPrefixBytes.Length).ToArray();

                if (ops.Length == 3)
                    magic = ops[2].PushData;

                break;
            }

            if (commitmentData != null)
                return (BitConverter.ToInt32(commitmentData), ((magic == null) ? (uint?)null : BitConverter.ToUInt32(magic)));

            return (null, null);
        }
    }
}
