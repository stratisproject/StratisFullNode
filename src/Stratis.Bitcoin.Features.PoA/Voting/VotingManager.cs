using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConcurrentCollections;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.PoA.Events;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Utilities;
using TracerAttributes;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public sealed class VotingManager : IDisposable
    {
        private readonly PoAConsensusOptions poaConsensusOptions;
        private readonly IBlockRepository blockRepository;
        private readonly ChainIndexer chainIndexer;
        private readonly IFederationManager federationManager;

        private IFederationHistory federationHistory;

        private readonly VotingDataEncoder votingDataEncoder;

        private readonly IPollResultExecutor pollResultExecutor;

        private readonly ISignals signals;

        private readonly INodeStats nodeStats;

        private readonly Network network;
        private readonly ILogger logger;

        private readonly IFinalizedBlockInfoRepository finalizedBlockInfo;

        /// <summary>Protects access to <see cref="scheduledVotingData"/>, <see cref="polls"/>, <see cref="pollsRepository"/>.</summary>
        private readonly object locker;

        /// <summary>All access should be protected by <see cref="locker"/>.</remarks>
        private readonly PollsRepository pollsRepository;

        private IIdleFederationMembersKicker idleFederationMembersKicker;

        /// <summary>In-memory collection of pending polls.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private List<Poll> polls;

        private SubscriptionToken blockConnectedSubscription;
        private SubscriptionToken blockDisconnectedSubscription;

        /// <summary>Collection of voting data that should be included in a block when it's mined.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private List<VotingData> scheduledVotingData;

        public bool IsInitialized { get; private set; }
        private bool isBusyReconstructing;

        private INodeLifetime nodeLifetime;

        public VotingManager(IFederationManager federationManager, ILoggerFactory loggerFactory, IPollResultExecutor pollResultExecutor,
            INodeStats nodeStats, DataFolder dataFolder, DBreezeSerializer dBreezeSerializer, ISignals signals,
            IFinalizedBlockInfoRepository finalizedBlockInfo,
            Network network,
            IBlockRepository blockRepository = null,
            ChainIndexer chainIndexer = null,
            INodeLifetime nodeLifetime = null)
        {
            this.federationManager = Guard.NotNull(federationManager, nameof(federationManager));
            this.pollResultExecutor = Guard.NotNull(pollResultExecutor, nameof(pollResultExecutor));
            this.signals = Guard.NotNull(signals, nameof(signals));
            this.nodeStats = Guard.NotNull(nodeStats, nameof(nodeStats));
            this.finalizedBlockInfo = Guard.NotNull(finalizedBlockInfo, nameof(finalizedBlockInfo));

            this.locker = new object();
            this.votingDataEncoder = new VotingDataEncoder(loggerFactory);
            this.scheduledVotingData = new List<VotingData>();
            this.pollsRepository = new PollsRepository(dataFolder, loggerFactory, dBreezeSerializer);
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.poaConsensusOptions = (PoAConsensusOptions)this.network.Consensus.Options;

            this.blockRepository = blockRepository;
            this.chainIndexer = chainIndexer;
            this.nodeLifetime = nodeLifetime;

            this.IsInitialized = false;
        }

        public void Initialize(IFederationHistory federationHistory, IIdleFederationMembersKicker idleFederationMembersKicker = null)
        {
            this.federationHistory = federationHistory;
            this.idleFederationMembersKicker = idleFederationMembersKicker;

            this.pollsRepository.Initialize();

            this.polls = this.pollsRepository.GetAllPolls();

            this.blockConnectedSubscription = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);
            this.blockDisconnectedSubscription = this.signals.Subscribe<BlockDisconnected>(this.OnBlockDisconnected);

            this.nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name, 1200);

            this.IsInitialized = true;

            this.logger.LogDebug("VotingManager initialized.");
        }

        /// <summary> Remove all polls that started on or after the given height.</summary>
        /// <param name="height">The height to clean polls from.</param>
        public void DeletePollsAfterHeight(int height)
        {
            this.logger.LogInformation($"Cleaning poll data from height {height}.");

            var idsToRemove = new List<int>();

            this.polls = this.pollsRepository.GetAllPolls();

            foreach (Poll poll in this.polls.Where(p => p.PollStartBlockData.Height >= height))
            {
                idsToRemove.Add(poll.Id);
            }

            if (idsToRemove.Any())
            {
                this.pollsRepository.DeletePollsAndSetHighestPollId(idsToRemove.ToArray());
                this.polls = this.pollsRepository.GetAllPolls();
            }
        }

        private IEnumerable<(ChainedHeader, Block)> BatchBlocksFrom(ChainedHeader previousBlock, int batchSize)
        {
            for (int height = previousBlock.Height + 1; ;)
            {
                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    throw new OperationCanceledException();

                var hashes = new List<uint256>();
                for (int i = 0; i < batchSize; i++)
                {
                    ChainedHeader header = this.chainIndexer.GetHeader(height + i);
                    if (header == null)
                        break;

                    if (header.Previous != previousBlock)
                        break;

                    hashes.Add(header.HashBlock);

                    previousBlock = header;
                }

                if (hashes.Count == 0)
                    yield break;

                List<Block> blocks = this.blockRepository.GetBlocks(hashes);
                for (int i = 0; i < blocks.Count && !this.nodeLifetime.ApplicationStopping.IsCancellationRequested; height++, i++)
                {
                    ChainedHeader header = this.chainIndexer.GetHeader(height);
                    yield return ((header, blocks[i]));
                }
            }
        }

        /// <summary> Reconstructs voting and poll data from a given height.</summary>
        /// <param name="height">The height to start reconstructing from.</param>
        public void ReconstructVotingDataFromHeightLocked(int height)
        {
            try
            {
                this.isBusyReconstructing = true;

                void Progress(int height)
                {
                    string progress = $"Reconstructing poll repository from voting data at height {height}.";
                    this.logger.LogInformation(progress);
                    this.signals.Publish(new RecontructFederationProgressEvent() { Progress = progress });
                }

                Progress(height);

                foreach ((ChainedHeader chainedHeader, Block block) in BatchBlocksFrom(this.chainIndexer.GetHeader(height), 1000))
                {
                    OnBlockConnected(new BlockConnected(new ChainedHeaderBlock(block, chainedHeader)));

                    if (chainedHeader.Height % 10000 == 0)
                        Progress(chainedHeader.Height);
                };
            }
            finally
            {
                this.isBusyReconstructing = false;
            }
        }

        /// <summary>Schedules a vote for the next time when the block will be mined.</summary>
        /// <exception cref="InvalidOperationException">Thrown in case caller is not a federation member.</exception>
        public void ScheduleVote(VotingData votingData)
        {
            this.EnsureInitialized();

            if (!this.federationManager.IsFederationMember)
            {
                this.logger.LogTrace("(-)[NOT_FED_MEMBER]");
                throw new InvalidOperationException("Not a federation member!");
            }

            lock (this.locker)
            {
                if (!this.scheduledVotingData.Any(v => v == votingData))
                    this.scheduledVotingData.Add(votingData);

                this.CleanFinishedPollsLocked();
            }

            this.logger.LogDebug("Vote was scheduled with key: {0}.", votingData.Key);
        }

        /// <summary>Provides a copy of scheduled voting data.</summary>
        public List<VotingData> GetScheduledVotes()
        {
            this.EnsureInitialized();

            lock (this.locker)
            {
                this.CleanFinishedPollsLocked();

                return new List<VotingData>(this.scheduledVotingData);
            }
        }

        /// <summary>Provides scheduled voting data and removes all items that were provided.</summary>
        /// <remarks>Used by miner.</remarks>
        public List<VotingData> GetAndCleanScheduledVotes()
        {
            this.EnsureInitialized();

            lock (this.locker)
            {
                this.CleanFinishedPollsLocked();

                List<VotingData> votingData = this.scheduledVotingData;

                this.scheduledVotingData = new List<VotingData>();

                if (votingData.Count > 0)
                    this.logger.LogDebug("{0} scheduled votes were taken.", votingData.Count);

                return votingData;
            }
        }

        /// <summary>Checks pending polls against finished polls and removes pending polls that will make no difference and basically are redundant.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private void CleanFinishedPollsLocked()
        {
            // We take polls that are not pending (collected enough votes in favor) but not executed yet (maxReorg blocks
            // didn't pass since the vote that made the poll pass). We can't just take not pending polls because of the
            // following scenario: federation adds a hash or fed member or does any other revertable action, then reverts
            // the action (removes the hash) and then reapplies it again. To allow for this scenario we have to exclude
            // executed polls here.
            List<Poll> finishedPolls = this.polls.Where(x => !x.IsPending && !x.IsExecuted).ToList();

            for (int i = this.scheduledVotingData.Count - 1; i >= 0; i--)
            {
                VotingData currentScheduledData = this.scheduledVotingData[i];

                // Remove scheduled voting data that can be found in finished polls that were not yet executed.
                if (finishedPolls.Any(x => x.VotingData == currentScheduledData))
                    this.scheduledVotingData.RemoveAt(i);
            }
        }

        /// <summary>Provides a collection of polls that are currently active.</summary>
        public List<Poll> GetPendingPolls()
        {
            this.EnsureInitialized();

            lock (this.locker)
            {
                return new List<Poll>(this.polls.Where(x => x.IsPending));

            }
        }

        /// <summary>Provides a collection of polls that are approved but not executed yet.</summary>
        public List<Poll> GetApprovedPolls()
        {
            this.EnsureInitialized();

            lock (this.locker)
            {
                return new List<Poll>(this.polls.Where(x => !x.IsPending));
            }
        }

        /// <summary>Provides a collection of polls that are approved and their results applied.</summary>
        public List<Poll> GetExecutedPolls()
        {
            this.EnsureInitialized();

            lock (this.locker)
            {
                return new List<Poll>(this.polls.Where(x => x.IsExecuted));
            }
        }

        /// <summary>
        /// Tells us whether we have already voted to boot a federation member.
        /// </summary>
        public bool AlreadyVotingFor(VoteKey voteKey, byte[] federationMemberBytes)
        {
            List<Poll> approvedPolls = this.GetApprovedPolls();

            if (approvedPolls.Any(x => !x.IsExecuted &&
                  x.VotingData.Key == voteKey && x.VotingData.Data.SequenceEqual(federationMemberBytes) &&
                  x.PubKeysHexVotedInFavor.Contains(this.federationManager.CurrentFederationKey.PubKey.ToHex())))
            {
                // We've already voted in a finished poll that's only awaiting execution.
                return true;
            }

            List<Poll> pendingPolls = this.GetPendingPolls();

            if (pendingPolls.Any(x => x.VotingData.Key == voteKey &&
                                       x.VotingData.Data.SequenceEqual(federationMemberBytes) &&
                                       x.PubKeysHexVotedInFavor.Contains(this.federationManager.CurrentFederationKey.PubKey.ToHex())))
            {
                // We've already voted in a pending poll.
                return true;
            }


            List<VotingData> scheduledVotes = this.GetScheduledVotes();

            if (scheduledVotes.Any(x => x.Key == voteKey && x.Data.SequenceEqual(federationMemberBytes)))
            {
                // We have the vote queued to be put out next time we mine a block.
                return true;
            }

            return false;
        }

        public bool IsFederationMember(PubKey pubKey)
        {
            return this.federationManager.GetFederationMembers().Any(fm => fm.PubKey == pubKey);
        }

        public List<IFederationMember> GetFederationFromExecutedPolls()
        {
            lock (this.locker)
            {
                var federation = new List<IFederationMember>(this.poaConsensusOptions.GenesisFederationMembers);

                IEnumerable<Poll> executedPolls = this.GetExecutedPolls().MemberPolls();
                foreach (Poll poll in executedPolls.OrderBy(a => a.PollExecutedBlockData.Height))
                {
                    IFederationMember federationMember = ((PoAConsensusFactory)(this.network.Consensus.ConsensusFactory)).DeserializeFederationMember(poll.VotingData.Data);

                    if (poll.VotingData.Key == VoteKey.AddFederationMember)
                        federation.Add(federationMember);
                    else if (poll.VotingData.Key == VoteKey.KickFederationMember)
                        federation.Remove(federationMember);
                }

                return federation;
            }
        }

        public void EnterStraxEra(List<IFederationMember> modifiedFederation)
        {
            // If we are accessing blocks prior to STRAX activation then the IsMultisigMember values for the members may be different. 
            for (int i = 0; i < modifiedFederation.Count; i++)
            {
                bool shouldBeMultisigMember = ((PoANetwork)this.network).StraxMiningMultisigMembers.Contains(modifiedFederation[i].PubKey);
                var member = (CollateralFederationMember)modifiedFederation[i];

                if (member.IsMultisigMember != shouldBeMultisigMember)
                {
                    // Clone the member if we will be changing the flag.
                    modifiedFederation[i] = new CollateralFederationMember(member.PubKey, shouldBeMultisigMember, member.CollateralAmount, member.CollateralMainchainAddress);
                }
            }
        }

        public List<IFederationMember> GetModifiedFederation(ChainedHeader chainedHeader)
        {
            return GetModifiedFederations(new[] { chainedHeader }).Single().federation;
        }

        public IEnumerable<(List<IFederationMember> federation, HashSet<IFederationMember> whoJoined)> GetModifiedFederations(IEnumerable<ChainedHeader> chainedHeaders)
        {
            lock (this.locker)
            {
                // Starting with the genesis federation...
                List<IFederationMember> modifiedFederation = new List<IFederationMember>(this.poaConsensusOptions.GenesisFederationMembers);
                Poll[] approvedPolls = this.GetApprovedPolls().MemberPolls().OrderBy(a => a.PollVotedInFavorBlockData.Height).ToArray();
                int pollIndex = 0;
                bool straxEra = false;
                int? multisigMinersApplicabilityHeight = this.federationManager.GetMultisigMinersApplicabilityHeight();

                foreach (ChainedHeader chainedHeader in chainedHeaders)
                {
                    var whoJoined = new HashSet<IFederationMember>();

                    if (!(this.network.Consensus.ConsensusFactory is PoAConsensusFactory poaConsensusFactory))
                    {
                        yield return (new List<IFederationMember>(this.poaConsensusOptions.GenesisFederationMembers), 
                            new HashSet<IFederationMember>((chainedHeader.Height != 0) ? new List<IFederationMember>() : this.poaConsensusOptions.GenesisFederationMembers));

                        continue;
                    }

                    if (!straxEra && (multisigMinersApplicabilityHeight != null && chainedHeader.Height >= multisigMinersApplicabilityHeight))
                    {
                        EnterStraxEra(modifiedFederation);
                        straxEra = true;
                    }

                    // Apply all polls that executed at or before the current height.
                    for (; pollIndex < approvedPolls.Length; pollIndex++)
                    {
                        // Modify the federation with the polls that would have been executed up to the given height.
                        Poll poll = approvedPolls[pollIndex];

                        // If it executed after the current height then exit this loop.
                        int pollExecutionHeight = poll.PollVotedInFavorBlockData.Height + (int)this.network.Consensus.MaxReorgLength;
                        if (pollExecutionHeight > chainedHeader.Height)
                            break;

                        IFederationMember federationMember = ((PoAConsensusFactory)(this.network.Consensus.ConsensusFactory)).DeserializeFederationMember(poll.VotingData.Data);

                        // Addition/removal.
                        if (poll.VotingData.Key == VoteKey.AddFederationMember)
                        {
                            if (!modifiedFederation.Contains(federationMember))
                            {
                                if (straxEra && federationMember is CollateralFederationMember collateralFederationMember)
                                {
                                    bool shouldBeMultisigMember = ((PoANetwork)this.network).StraxMiningMultisigMembers.Contains(federationMember.PubKey);
                                    if (collateralFederationMember.IsMultisigMember != shouldBeMultisigMember)
                                        collateralFederationMember.IsMultisigMember = shouldBeMultisigMember;
                                }

                                if (pollExecutionHeight == chainedHeader.Height)
                                    whoJoined.Add(federationMember);

                                modifiedFederation.Add(federationMember);
                            }
                        }
                        else if (poll.VotingData.Key == VoteKey.KickFederationMember)
                        {
                            if (modifiedFederation.Contains(federationMember))
                            {
                                modifiedFederation.Remove(federationMember);
                            }
                        }
                    }

                    yield return (new List<IFederationMember>(modifiedFederation), whoJoined);
                }
            }
        }

        public IFederationMember GetMemberVotedOn(VotingData votingData)
        {
            if (votingData.Key != VoteKey.AddFederationMember && votingData.Key != VoteKey.KickFederationMember)
                return null;

            if (!(this.network.Consensus.ConsensusFactory is PoAConsensusFactory poaConsensusFactory))
                return null;

            return poaConsensusFactory.DeserializeFederationMember(votingData.Data);
        }

        private bool IsVotingOnMultisigMember(VotingData votingData)
        {
            IFederationMember member = GetMemberVotedOn(votingData);

            // Ignore votes on multisig-members.
            return member != null && this.federationManager.IsMultisigMember(member.PubKey);
        }

        private void OnBlockConnected(BlockConnected blockConnected)
        {
            try
            {
                ChainedHeaderBlock chBlock = blockConnected.ConnectedBlock;
                HashHeightPair newFinalizedHash = this.finalizedBlockInfo.GetFinalizedBlockInfo();

                lock (this.locker)
                {
                    if (this.isBusyReconstructing)
                    {
                        foreach (Poll poll in this.GetApprovedPolls().ToList())
                        {
                            if (blockConnected.ConnectedBlock.ChainedHeader.Height - poll.PollVotedInFavorBlockData.Height == this.network.Consensus.MaxReorgLength)
                            {
                                this.logger.LogDebug("Applying poll '{0}'.", poll);
                                this.pollResultExecutor.ApplyChange(poll.VotingData);

                                poll.PollExecutedBlockData = new HashHeightPair(chBlock.ChainedHeader);
                                this.pollsRepository.UpdatePoll(poll);
                            }
                        }
                    }
                    else
                    {
                        foreach (Poll poll in this.GetApprovedPolls().Where(x => x.PollVotedInFavorBlockData.Hash == newFinalizedHash.Hash).ToList())
                        {
                            this.logger.LogDebug("Applying poll '{0}'.", poll);
                            this.pollResultExecutor.ApplyChange(poll.VotingData);

                            poll.PollExecutedBlockData = new HashHeightPair(chBlock.ChainedHeader);
                            this.pollsRepository.UpdatePoll(poll);
                        }
                    }
                }

                byte[] rawVotingData = this.votingDataEncoder.ExtractRawVotingData(chBlock.Block.Transactions[0]);

                if (rawVotingData == null)
                {
                    this.logger.LogTrace("(-)[NO_VOTING_DATA]");
                    return;
                }

                string fedMemberKeyHex = this.federationHistory.GetFederationMemberForBlock(chBlock.ChainedHeader).PubKey.ToHex();

                List<VotingData> votingDataList = this.votingDataEncoder.Decode(rawVotingData);

                this.logger.LogDebug("Applying {0} voting data items included in a block by '{1}'.", votingDataList.Count, fedMemberKeyHex);

                lock (this.locker)
                {
                    foreach (VotingData data in votingDataList)
                    {
                        if (this.federationManager.CurrentFederationKey?.PubKey.ToHex() == fedMemberKeyHex)
                        {
                            // Any votes found in the block is no longer scheduled.
                            // This avoids clinging to votes scheduled during IBD.
                            if (this.scheduledVotingData.Any(v => v == data))
                                this.scheduledVotingData.Remove(data);
                        }

                        if (this.IsVotingOnMultisigMember(data))
                            continue;

                        Poll poll = this.polls.SingleOrDefault(x => x.VotingData == data && x.IsPending);

                        if (poll == null)
                        {
                            // Ensures that highestPollId can't be changed before the poll is committed.
                            this.pollsRepository.Synchronous(() =>
                            {
                                poll = new Poll()
                                {
                                    Id = this.pollsRepository.GetHighestPollId() + 1,
                                    PollVotedInFavorBlockData = null,
                                    PollExecutedBlockData = null,
                                    PollStartBlockData = new HashHeightPair(chBlock.ChainedHeader),
                                    VotingData = data,
                                    PubKeysHexVotedInFavor = new List<string>() { fedMemberKeyHex }
                                };

                                this.polls.Add(poll);
                                this.pollsRepository.AddPolls(poll);

                                this.logger.LogDebug("New poll was created: '{0}'.", poll);
                            });
                        }
                        else if (!poll.PubKeysHexVotedInFavor.Contains(fedMemberKeyHex))
                        {
                            poll.PubKeysHexVotedInFavor.Add(fedMemberKeyHex);
                            this.pollsRepository.UpdatePoll(poll);

                            this.logger.LogDebug("Voted on existing poll: '{0}'.", poll);
                        }
                        else
                        {
                            this.logger.LogDebug("Fed member '{0}' already voted for this poll. Ignoring his vote. Poll: '{1}'.", fedMemberKeyHex, poll);
                        }

                        List<IFederationMember> modifiedFederation = this.federationManager.GetFederationMembers();

                        var fedMembersHex = new ConcurrentHashSet<string>(modifiedFederation.Select(x => x.PubKey.ToHex()));

                        // Member that were about to be kicked when voting started don't participate.
                        if (this.idleFederationMembersKicker != null)
                        {
                            ChainedHeader chainedHeader = chBlock.ChainedHeader.GetAncestor(poll.PollStartBlockData.Height);

                            if (chainedHeader?.Header == null)
                            {
                                this.logger.LogWarning("Couldn't retrieve header for block at height-hash: {0}-{1}.", poll.PollStartBlockData.Height, poll.PollStartBlockData.Hash?.ToString());

                                Guard.NotNull(chainedHeader, nameof(chainedHeader));
                                Guard.NotNull(chainedHeader.Header, nameof(chainedHeader.Header));
                            }

                            foreach (IFederationMember member in modifiedFederation)
                            {
                                if (this.idleFederationMembersKicker.ShouldMemberBeKicked(member, chainedHeader, chBlock.ChainedHeader, out _))
                                {
                                    fedMembersHex.TryRemove(member.PubKey.ToHex());
                                }
                            }
                        }

                        // It is possible that there is a vote from a federation member that was deleted from the federation.
                        // Do not count votes from entities that are not active fed members.
                        int validVotesCount = poll.PubKeysHexVotedInFavor.Count(x => fedMembersHex.Contains(x));

                        int requiredVotesCount = (fedMembersHex.Count / 2) + 1;

                        this.logger.LogDebug("Fed members count: {0}, valid votes count: {1}, required votes count: {2}.", fedMembersHex.Count, validVotesCount, requiredVotesCount);

                        if (validVotesCount < requiredVotesCount)
                            continue;

                        poll.PollVotedInFavorBlockData = new HashHeightPair(chBlock.ChainedHeader);
                        this.pollsRepository.UpdatePoll(poll);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, ex.ToString());
                throw;
            }
        }

        private void OnBlockDisconnected(BlockDisconnected blockDisconnected)
        {
            ChainedHeaderBlock chBlock = blockDisconnected.DisconnectedBlock;

            lock (this.locker)
            {
                foreach (Poll poll in this.polls.Where(x => !x.IsPending && x.PollExecutedBlockData?.Hash == chBlock.ChainedHeader.HashBlock).ToList())
                {
                    this.logger.LogDebug("Reverting poll execution '{0}'.", poll);
                    this.pollResultExecutor.RevertChange(poll.VotingData);

                    poll.PollExecutedBlockData = null;
                    this.pollsRepository.UpdatePoll(poll);
                }
            }

            byte[] rawVotingData = this.votingDataEncoder.ExtractRawVotingData(chBlock.Block.Transactions[0]);

            if (rawVotingData == null)
            {
                this.logger.LogTrace("(-)[NO_VOTING_DATA]");
                return;
            }

            List<VotingData> votingDataList = this.votingDataEncoder.Decode(rawVotingData);
            votingDataList.Reverse();

            lock (this.locker)
            {
                foreach (VotingData votingData in votingDataList)
                {
                    if (this.IsVotingOnMultisigMember(votingData))
                        continue;

                    // If the poll is pending, that's the one we want. There should be maximum 1 of these.
                    Poll targetPoll = this.polls.SingleOrDefault(x => x.VotingData == votingData && x.IsPending);

                    // Otherwise, get the most recent poll. There could currently be unlimited of these, though they're harmless.
                    if (targetPoll == null)
                    {
                        targetPoll = this.polls.Last(x => x.VotingData == votingData);
                    }

                    this.logger.LogDebug("Reverting poll voting in favor: '{0}'.", targetPoll);

                    if (targetPoll.PollVotedInFavorBlockData == new HashHeightPair(chBlock.ChainedHeader))
                    {
                        targetPoll.PollVotedInFavorBlockData = null;

                        this.pollsRepository.UpdatePoll(targetPoll);
                    }

                    // Pub key of a fed member that created voting data.
                    string fedMemberKeyHex = this.federationHistory.GetFederationMemberForBlock(chBlock.ChainedHeader).PubKey.ToHex();

                    targetPoll.PubKeysHexVotedInFavor.Remove(fedMemberKeyHex);

                    if (targetPoll.PubKeysHexVotedInFavor.Count == 0)
                    {
                        this.polls.Remove(targetPoll);
                        this.pollsRepository.RemovePolls(targetPoll.Id);

                        this.logger.LogDebug("Poll with Id {0} was removed.", targetPoll.Id);
                    }
                }
            }
        }

        [NoTrace]
        private void AddComponentStats(StringBuilder log)
        {
            log.AppendLine(">> Voting & Poll Data");

            // Use the polls list directly as opposed to the locked versions of them for console reporting.
            List<Poll> pendingPolls = this.polls.Where(x => x.IsPending).ToList();
            List<Poll> approvedPolls = this.polls.Where(x => !x.IsPending).ToList();
            List<Poll> executedPolls = this.polls.Where(x => x.IsExecuted).ToList();

            log.AppendLine("Member Polls".PadRight(LoggingConfiguration.ColumnLength) + $": Pending: {pendingPolls.MemberPolls().Count} Approved: {approvedPolls.MemberPolls().Count} Executed : {executedPolls.MemberPolls().Count}");
            log.AppendLine("Whitelist Polls".PadRight(LoggingConfiguration.ColumnLength) + $": Pending: {pendingPolls.WhitelistPolls().Count} Approved: {approvedPolls.WhitelistPolls().Count} Executed : {executedPolls.WhitelistPolls().Count}");
            log.AppendLine("Scheduled Votes".PadRight(LoggingConfiguration.ColumnLength) + ": " + this.scheduledVotingData.Count);
            log.AppendLine("Scheduled votes will be added to the next block this node mines.");
            log.AppendLine();
        }

        [NoTrace]
        private void EnsureInitialized()
        {
            if (!this.IsInitialized)
            {
                throw new Exception("VotingManager is not initialized. Check that voting is enabled in PoAConsensusOptions.");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.signals.Unsubscribe(this.blockConnectedSubscription);
            this.signals.Unsubscribe(this.blockDisconnectedSubscription);

            this.pollsRepository.Dispose();
        }
    }
}
