﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConcurrentCollections;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Features.BlockStore;
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

        /// <summary>Protects access to <see cref="scheduledVotingData"/>, <see cref="polls"/>, <see cref="PollsRepository"/>.</summary>
        private readonly object locker;

        /// <summary>All access should be protected by <see cref="locker"/>.</remarks>
        public PollsRepository PollsRepository { get; private set; }

        private IIdleFederationMembersKicker idleFederationMembersKicker;
        private readonly INodeLifetime nodeLifetime;

        /// <summary>In-memory collection of pending polls.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private PollsCollection polls;

        private SubscriptionToken blockConnectedSubscription;
        private SubscriptionToken blockDisconnectedSubscription;

        /// <summary>Collection of voting data that should be included in a block when it's mined.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private List<VotingData> scheduledVotingData;

        private int blocksProcessed;

        public long blocksProcessingTime;

        internal bool isInitialized;

        public VotingManager(IFederationManager federationManager, IPollResultExecutor pollResultExecutor, INodeStats nodeStats,
            DataFolder dataFolder, DBreezeSerializer dBreezeSerializer, ISignals signals, Network network,
            IBlockRepository blockRepository = null,
            ChainIndexer chainIndexer = null,
            INodeLifetime nodeLifetime = null)
        {
            this.federationManager = Guard.NotNull(federationManager, nameof(federationManager));
            this.pollResultExecutor = Guard.NotNull(pollResultExecutor, nameof(pollResultExecutor));
            this.signals = Guard.NotNull(signals, nameof(signals));
            this.nodeStats = Guard.NotNull(nodeStats, nameof(nodeStats));

            this.locker = new object();
            this.votingDataEncoder = new VotingDataEncoder();
            this.scheduledVotingData = new List<VotingData>();
            this.PollsRepository = new PollsRepository(dataFolder, dBreezeSerializer, chainIndexer);

            this.logger = LogManager.GetCurrentClassLogger();
            this.network = network;
            this.poaConsensusOptions = (PoAConsensusOptions)this.network.Consensus.Options;

            Guard.Assert(this.poaConsensusOptions.PollExpiryBlocks != 0);

            this.blockRepository = blockRepository;
            this.chainIndexer = chainIndexer;
            this.nodeLifetime = nodeLifetime;

            this.isInitialized = false;
        }

        public void Initialize(IFederationHistory federationHistory, IIdleFederationMembersKicker idleFederationMembersKicker = null)
        {
            this.federationHistory = federationHistory;
            this.idleFederationMembersKicker = idleFederationMembersKicker;

            this.PollsRepository.Initialize();

            this.PollsRepository.WithTransaction(transaction => this.polls = new PollsCollection(this.PollsRepository.GetAllPolls(transaction)));

            this.blockConnectedSubscription = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);
            this.blockDisconnectedSubscription = this.signals.Subscribe<BlockDisconnected>(this.OnBlockDisconnected);

            this.nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name, 1200);

            this.isInitialized = true;

            this.logger.Debug("VotingManager initialized.");
        }

        /// <summary>Schedules a vote for the next time when the block will be mined.</summary>
        /// <exception cref="InvalidOperationException">Thrown in case caller is not a federation member.</exception>
        public void ScheduleVote(VotingData votingData)
        {
            this.EnsureInitialized();

            if (!this.federationManager.IsFederationMember)
            {
                this.logger.Trace("(-)[NOT_FED_MEMBER]");
                throw new InvalidOperationException("Not a federation member!");
            }

            lock (this.locker)
            {
                if (!this.scheduledVotingData.Any(v => v == votingData))
                    this.scheduledVotingData.Add(votingData);

                this.SanitizeScheduledPollsLocked();
            }

            this.logger.Debug("Vote was scheduled with key: {0}.", votingData.Key);
        }

        /// <summary>Provides a copy of scheduled voting data.</summary>
        public List<VotingData> GetScheduledVotes()
        {
            this.EnsureInitialized();

            lock (this.locker)
            {
                this.SanitizeScheduledPollsLocked();

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
                this.SanitizeScheduledPollsLocked();

                List<VotingData> votingData = this.scheduledVotingData;

                this.scheduledVotingData = new List<VotingData>();

                if (votingData.Count > 0)
                    this.logger.Debug("{0} scheduled votes were taken.", votingData.Count);

                return votingData;
            }
        }

        /// <summary>Performs sanity checks against scheduled votes and removes any conflicts.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private void SanitizeScheduledPollsLocked()
        {
            // Sanitize the scheduled votes.
            // Remove scheduled votes that are in pending polls or non-executed approved polls.
            lock (this.locker)
            {
                List<Poll> pendingPolls = this.GetPendingPolls().ToList();
                List<Poll> approvedPolls = this.GetApprovedPolls().Where(x => !x.IsExecuted).ToList();

                bool IsTooOldToVoteOn(Poll poll) => poll.IsPending && (this.chainIndexer.Tip.Height - poll.PollStartBlockData.Height) >= this.poaConsensusOptions.PollExpiryBlocks;

                bool IsValid(VotingData currentScheduledData)
                {
                    // Remove scheduled voting data that can be found in pending polls.
                    if (pendingPolls.Any(x => x.VotingData == currentScheduledData && IsTooOldToVoteOn(x)))
                        return false;

                    // Remove scheduled voting data that can be found in finished polls that were not yet executed.
                    if (approvedPolls.Any(x => x.VotingData == currentScheduledData))
                        return false;

                    return true;
                }

                this.scheduledVotingData = this.scheduledVotingData.Where(d => IsValid(d)).ToList();
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
                return new List<Poll>(this.polls.Where(x => x.IsApproved));
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

        /// <summary>Provides a collection of polls that are expired.</summary>
        public List<Poll> GetExpiredPolls()
        {
            this.EnsureInitialized();

            lock (this.locker)
            {
                return new List<Poll>(this.polls.Where(x => x.IsExpired));
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
                  x.PubKeysHexVotedInFavor.Any(v => v.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex())))
            {
                // We've already voted in a finished poll that's only awaiting execution.
                return true;
            }

            List<Poll> pendingPolls = this.GetPendingPolls();

            if (pendingPolls.Any(x => x.VotingData.Key == voteKey &&
                                       x.VotingData.Data.SequenceEqual(federationMemberBytes) &&
                                       x.PubKeysHexVotedInFavor.Any(v => v.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex())))
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
                    {
                        if (federationMember is CollateralFederationMember colMember2 && federation.Any(m => m is CollateralFederationMember colMember && colMember.CollateralMainchainAddress == colMember2.CollateralMainchainAddress))
                        {
                            this.logger.Debug("Not adding member '{0}' with duplicate collateral address '{1}'.", federationMember.PubKey.ToHex(), colMember2.CollateralMainchainAddress);
                            continue;
                        }

                        federation.Add(federationMember);
                    }
                    else if (poll.VotingData.Key == VoteKey.KickFederationMember)
                    {
                        federation.Remove(federationMember);
                    }
                }

                return federation;
            }
        }

        public int LastKnownFederationHeight()
        {
            return (this.PollsRepository.CurrentTip?.Height ?? 0) + (int)this.network.Consensus.MaxReorgLength - 1;
        }

        public bool CanGetFederationForBlock(ChainedHeader chainedHeader)
        {
            return chainedHeader.Height <= LastKnownFederationHeight();
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
            return this.federationHistory.GetFederationForBlock(chainedHeader);
        }

        public IEnumerable<(List<IFederationMember> federation, HashSet<IFederationMember> whoJoined)> GetFederationsForHeights(int startHeight, int endHeight)
        {
            lock (this.locker)
            {
                // Starting with the genesis federation...
                List<IFederationMember> modifiedFederation = new List<IFederationMember>(this.poaConsensusOptions.GenesisFederationMembers);
                Poll[] approvedPolls = this.GetApprovedPolls().MemberPolls().OrderBy(a => a.PollVotedInFavorBlockData.Height).ToArray();
                int pollIndex = 0;
                bool straxEra = false;
                int? multisigMinersApplicabilityHeight = this.federationManager.GetMultisigMinersApplicabilityHeight();

                for (int height = startHeight; height <= endHeight; height++)
                {
                    var whoJoined = new HashSet<IFederationMember>();

                    if (!(this.network.Consensus.ConsensusFactory is PoAConsensusFactory poaConsensusFactory))
                    {
                        yield return (new List<IFederationMember>(this.poaConsensusOptions.GenesisFederationMembers),
                            new HashSet<IFederationMember>((height != 0) ? new List<IFederationMember>() : this.poaConsensusOptions.GenesisFederationMembers));

                        continue;
                    }

                    if (!straxEra && (multisigMinersApplicabilityHeight != null && height >= multisigMinersApplicabilityHeight))
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
                        if (pollExecutionHeight > height)
                            break;

                        IFederationMember federationMember = ((PoAConsensusFactory)(this.network.Consensus.ConsensusFactory)).DeserializeFederationMember(poll.VotingData.Data);

                        // Addition/removal.
                        if (poll.VotingData.Key == VoteKey.AddFederationMember)
                        {
                            if (!modifiedFederation.Contains(federationMember))
                            {
                                if (straxEra && federationMember is CollateralFederationMember collateralFederationMember)
                                {
                                    if (modifiedFederation.Any(m => m is CollateralFederationMember colMember && colMember.CollateralMainchainAddress == collateralFederationMember.CollateralMainchainAddress))
                                    {
                                        this.logger.Debug("Not adding member '{0}' with duplicate collateral address '{1}'.", collateralFederationMember.PubKey.ToHex(), collateralFederationMember.CollateralMainchainAddress);
                                        continue;
                                    }

                                    bool shouldBeMultisigMember = ((PoANetwork)this.network).StraxMiningMultisigMembers.Contains(federationMember.PubKey);
                                    if (collateralFederationMember.IsMultisigMember != shouldBeMultisigMember)
                                        collateralFederationMember.IsMultisigMember = shouldBeMultisigMember;
                                }

                                if (pollExecutionHeight == height)
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
            if (member == null)
                return false;

            // Ignore votes on multisig-members.
            return this.federationManager.IsMultisigMember(member.PubKey);
        }

        private bool IsPollExpiredAt(Poll poll, ChainedHeader chainedHeader)
        {
            if (chainedHeader == null)
                return false;

            return Math.Max(poll.PollStartBlockData.Height + this.poaConsensusOptions.PollExpiryBlocks, this.poaConsensusOptions.Release1100ActivationHeight) <= chainedHeader.Height;
        }

        private void ProcessBlock(DBreeze.Transactions.Transaction transaction, ChainedHeaderBlock chBlock)
        {
            long flagFall = DateTime.Now.Ticks;

            try
            {
                lock (this.locker)
                {
                    bool pollsRepositoryModified = false;

                    foreach (Poll poll in this.GetPendingPolls().Where(x => this.IsPollExpiredAt(x, chBlock.ChainedHeader)).ToList())
                    {
                        this.logger.Debug("Expiring poll '{0}'.", poll);

                        // Flag the poll as expired. The "PollVotedInFavorBlockData" will always be null at this point due to the "GetPendingPolls" filter above.
                        // The value of the hash is not significant but we set it to a non-zero value to prevent the field from being de-serialized as null.
                        poll.IsExpired = true;
                        this.polls.OnPendingStatusChanged(poll);
                        this.PollsRepository.UpdatePoll(transaction, poll);
                        pollsRepositoryModified = true;
                    }

                    foreach (Poll poll in this.GetApprovedPolls())
                    {
                        if (poll.IsExpired || chBlock.ChainedHeader.Height != (poll.PollVotedInFavorBlockData.Height + this.network.Consensus.MaxReorgLength))
                            continue;

                        this.logger.Debug("Applying poll '{0}'.", poll);
                        this.pollResultExecutor.ApplyChange(poll.VotingData);

                        poll.PollExecutedBlockData = new HashHeightPair(chBlock.ChainedHeader);
                        this.PollsRepository.UpdatePoll(transaction, poll);

                        pollsRepositoryModified = true;
                    }

                    if (this.federationManager.GetMultisigMinersApplicabilityHeight() == chBlock.ChainedHeader.Height)
                        this.federationManager.UpdateMultisigMiners(true);

                    byte[] rawVotingData = this.votingDataEncoder.ExtractRawVotingData(chBlock.Block.Transactions[0]);

                    if (rawVotingData == null)
                    {
                        this.PollsRepository.SaveCurrentTip(pollsRepositoryModified ? transaction : null, chBlock.ChainedHeader);
                        return;
                    }

                    IFederationMember member = this.federationHistory.GetFederationMemberForBlock(chBlock.ChainedHeader);
                    if (member == null)
                    {
                        this.logger.Error("The block was mined by a non-federation-member!");
                        this.logger.Trace("(-)[ALIEN_BLOCK]");

                        this.PollsRepository.SaveCurrentTip(pollsRepositoryModified ? transaction : null, chBlock.ChainedHeader);
                        return;
                    }

                    PubKey fedMemberKey = member.PubKey;

                    string fedMemberKeyHex = fedMemberKey.ToHex();

                    List<VotingData> votingDataList = this.votingDataEncoder.Decode(rawVotingData);

                    this.logger.Debug("Applying {0} voting data items included in a block by '{1}'.", votingDataList.Count, fedMemberKeyHex);

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

                            Poll poll = this.polls.GetPendingPollByVotingData(data);

                            if (poll == null)
                            {
                                // Ensures that highestPollId can't be changed before the poll is committed.
                                this.PollsRepository.Synchronous(() =>
                                {
                                    poll = new Poll()
                                    {
                                        Id = this.PollsRepository.GetHighestPollId() + 1,
                                        PollVotedInFavorBlockData = null,
                                        PollExecutedBlockData = null,
                                        PollStartBlockData = new HashHeightPair(chBlock.ChainedHeader),
                                        VotingData = data,
                                        PubKeysHexVotedInFavor = new List<Vote>() { new Vote() { PubKey = fedMemberKeyHex, Height = chBlock.ChainedHeader.Height } }
                                    };

                                    this.polls.Add(poll);

                                    this.PollsRepository.AddPolls(transaction, poll);
                                    pollsRepositoryModified = true;

                                    this.logger.Debug("New poll was created: '{0}'.", poll);
                                });
                            }
                            else if (!poll.PubKeysHexVotedInFavor.Any(v => v.PubKey == fedMemberKeyHex))
                            {
                                poll.PubKeysHexVotedInFavor.Add(new Vote() { PubKey = fedMemberKeyHex, Height = chBlock.ChainedHeader.Height });
                                this.PollsRepository.UpdatePoll(transaction, poll);
                                pollsRepositoryModified = true;

                                this.logger.Debug("Voted on existing poll: '{0}'.", poll);
                            }
                            else
                            {
                                this.logger.Debug("Fed member '{0}' already voted for this poll. Ignoring his vote. Poll: '{1}'.", fedMemberKeyHex, poll);
                            }

                            List<IFederationMember> modifiedFederation = this.federationManager.GetFederationMembers();

                            var fedMembersHex = new ConcurrentHashSet<string>(modifiedFederation.Select(x => x.PubKey.ToHex()));

                            // Member that were about to be kicked when voting started don't participate.
                            if (this.idleFederationMembersKicker != null)
                            {
                                ChainedHeader chainedHeader = chBlock.ChainedHeader.GetAncestor(poll.PollStartBlockData.Height);

                                if (chainedHeader?.Header == null)
                                {
                                    chainedHeader = this.chainIndexer.GetHeader(poll.PollStartBlockData.Hash);
                                    if (chainedHeader == null)
                                    {
                                        this.logger.Warn("Couldn't retrieve header for block at height-hash: {0}-{1}.", poll.PollStartBlockData.Height, poll.PollStartBlockData.Hash?.ToString());

                                        Guard.NotNull(chainedHeader, nameof(chainedHeader));
                                        Guard.NotNull(chainedHeader.Header, nameof(chainedHeader.Header));
                                    }
                                }

                                foreach (IFederationMember miner in modifiedFederation)
                                {
                                    if (this.idleFederationMembersKicker.ShouldMemberBeKicked(miner, chainedHeader, chBlock.ChainedHeader, out _))
                                    {
                                        fedMembersHex.TryRemove(miner.PubKey.ToHex());
                                    }
                                }
                            }

                            // It is possible that there is a vote from a federation member that was deleted from the federation.
                            // Do not count votes from entities that are not active fed members.
                            int validVotesCount = poll.PubKeysHexVotedInFavor.Count(x => fedMembersHex.Contains(x.PubKey));

                            int requiredVotesCount = (fedMembersHex.Count / 2) + 1;

                            this.logger.Debug("Fed members count: {0}, valid votes count: {1}, required votes count: {2}.", fedMembersHex.Count, validVotesCount, requiredVotesCount);

                            if (validVotesCount < requiredVotesCount)
                                continue;

                            poll.PollVotedInFavorBlockData = new HashHeightPair(chBlock.ChainedHeader);
                            this.polls.OnPendingStatusChanged(poll);

                            this.PollsRepository.UpdatePoll(transaction, poll);
                            pollsRepositoryModified = true;
                        }
                    }

                    this.PollsRepository.SaveCurrentTip(pollsRepositoryModified ? transaction : null, chBlock.ChainedHeader);
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, ex.ToString());
                throw;
            }
            finally
            {
                long timeConsumed = DateTime.Now.Ticks - flagFall;

                this.blocksProcessed++;
                this.blocksProcessingTime += timeConsumed;
            }
        }

        private void UnProcessBlock(DBreeze.Transactions.Transaction transaction, ChainedHeaderBlock chBlock)
        {
            bool pollsRepositoryModified = false;

            lock (this.locker)
            {
                foreach (Poll poll in this.polls.Where(x => !x.IsPending && x.PollExecutedBlockData?.Hash == chBlock.ChainedHeader.HashBlock).ToList())
                {
                    this.logger.Debug("Reverting poll execution '{0}'.", poll);
                    this.pollResultExecutor.RevertChange(poll.VotingData);

                    poll.PollExecutedBlockData = null;
                    this.PollsRepository.UpdatePoll(transaction, poll);
                    pollsRepositoryModified = true;
                }

                foreach (Poll poll in this.polls.Where(x => x.IsExpired && !IsPollExpiredAt(x, chBlock.ChainedHeader.Previous)).ToList())
                {
                    this.logger.Debug("Reverting poll expiry '{0}'.", poll);

                    // Revert back to null as this field would have been when the poll was expired.
                    poll.IsExpired = false;
                    this.polls.OnPendingStatusChanged(poll);
                    this.PollsRepository.UpdatePoll(transaction, poll);
                    pollsRepositoryModified = true;
                }

                if (this.federationManager.GetMultisigMinersApplicabilityHeight() == chBlock.ChainedHeader.Height)
                    this.federationManager.UpdateMultisigMiners(false);
            }

            byte[] rawVotingData = this.votingDataEncoder.ExtractRawVotingData(chBlock.Block.Transactions[0]);

            if (rawVotingData == null)
            {
                this.logger.Trace("(-)[NO_VOTING_DATA]");

                this.PollsRepository.SaveCurrentTip(pollsRepositoryModified ? transaction : null, chBlock.ChainedHeader.Previous);
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

                    this.logger.Debug("Reverting poll voting in favor: '{0}'.", targetPoll);

                    if (targetPoll.PollVotedInFavorBlockData == new HashHeightPair(chBlock.ChainedHeader))
                    {
                        targetPoll.PollVotedInFavorBlockData = null;
                        this.polls.OnPendingStatusChanged(targetPoll);

                        this.PollsRepository.UpdatePoll(transaction, targetPoll);
                        pollsRepositoryModified = true;
                    }

                    // Pub key of a fed member that created voting data.
                    string fedMemberKeyHex = this.federationHistory.GetFederationMemberForBlock(chBlock.ChainedHeader).PubKey.ToHex();
                    int voteIndex = targetPoll.PubKeysHexVotedInFavor.FindIndex(v => v.PubKey == fedMemberKeyHex);
                    if (voteIndex >= 0)
                    {
                        targetPoll.PubKeysHexVotedInFavor.RemoveAt(voteIndex);

                        if (targetPoll.PubKeysHexVotedInFavor.Count == 0)
                        {
                            this.polls.Remove(targetPoll);
                            this.PollsRepository.RemovePolls(transaction, targetPoll.Id);
                            pollsRepositoryModified = true;

                            this.logger.Debug("Poll with Id {0} was removed.", targetPoll.Id);
                        }
                    }
                }

                this.PollsRepository.SaveCurrentTip(pollsRepositoryModified ? transaction : null, chBlock.ChainedHeader.Previous);
            }
        }

        public ChainedHeader GetPollsRepositoryTip()
        {
            return (this.PollsRepository.CurrentTip == null) ? null : this.chainIndexer.GetHeader(this.PollsRepository.CurrentTip.Hash);
        }

        public List<IFederationMember> GetFederationAtPollsRepositoryTip(ChainedHeader repoTip)
        {
            if (repoTip == null)
                return new List<IFederationMember>(((PoAConsensusOptions)this.network.Consensus.Options).GenesisFederationMembers);

            return this.GetModifiedFederation(repoTip);
        }

        public List<IFederationMember> GetLastKnownFederation()
        {
            // If too far behind to accurately determine the federation then just take the last known federation. 
            if (((this.PollsRepository.CurrentTip?.Height ?? 0) + this.network.Consensus.MaxReorgLength) <= this.chainIndexer.Tip.Height)
            {
                ChainedHeader chainedHeader = this.chainIndexer.Tip.GetAncestor((int)(this.PollsRepository.CurrentTip?.Height ?? 0) + (int)this.network.Consensus.MaxReorgLength - 1);
                return this.GetModifiedFederation(chainedHeader);
            }

            return this.GetModifiedFederation(this.chainIndexer.Tip);
        }

        internal bool Synchronize(ChainedHeader newTip)
        {
            if (newTip?.HashBlock == this.PollsRepository.CurrentTip?.Hash)
                return true;

            ChainedHeader repoTip = GetPollsRepositoryTip();

            bool bSuccess = true;

            this.PollsRepository.Synchronous(() =>
            {
                // Remove blocks as required.
                if (repoTip != null)
                {
                    ChainedHeader fork = repoTip.FindFork(newTip);

                    if (repoTip.Height > fork.Height)
                    {
                        this.PollsRepository.WithTransaction(transaction =>
                        {
                            List<IFederationMember> modifiedFederation = this.GetFederationAtPollsRepositoryTip(repoTip);

                            for (ChainedHeader header = repoTip; header.Height > fork.Height; header = header.Previous)
                            {
                                Block block = this.blockRepository.GetBlock(header.HashBlock);

                                this.UnProcessBlock(transaction, new ChainedHeaderBlock(block, header));
                            }

                            transaction.Commit();
                        });

                        repoTip = fork;
                    }
                }

                // Add blocks as required.
                var headers = new List<ChainedHeader>();
                for (int height = (repoTip?.Height ?? 0) + 1; height <= newTip.Height; height++)
                {
                    ChainedHeader header = this.chainIndexer.GetHeader(height);
                    headers.Add(header);
                }

                if (headers.Count > 0)
                {
                    this.PollsRepository.WithTransaction(transaction =>
                    {
                        int i = 0;
                        foreach (Block block in this.blockRepository.EnumerateBatch(headers))
                        {
                            if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                            {
                                this.logger.Trace("(-)[NODE_DISPOSED]");
                                this.PollsRepository.SaveCurrentTip(transaction);
                                transaction.Commit();

                                bSuccess = false;
                                return;
                            }

                            ChainedHeader header = headers[i++];
                            this.ProcessBlock(transaction, new ChainedHeaderBlock(block, header));

                            if (header.Height % 10000 == 0)
                            {
                                this.logger.Info($"Synchronizing voting data at height {header.Height}.");
                            }
                        }

                        this.PollsRepository.SaveCurrentTip(transaction);

                        transaction.Commit();
                    });
                }
            });

            return bSuccess;
        }

        private void OnBlockConnected(BlockConnected blockConnected)
        {
            this.PollsRepository.Synchronous(() =>
            {
                if (this.Synchronize(blockConnected.ConnectedBlock.ChainedHeader.Previous))
                {
                    this.PollsRepository.WithTransaction(transaction =>
                    {
                        this.ProcessBlock(transaction, blockConnected.ConnectedBlock);
                        transaction.Commit();
                    });
                }
            });
        }

        private void OnBlockDisconnected(BlockDisconnected blockDisconnected)
        {
            this.PollsRepository.Synchronous(() =>
            {
                if (this.Synchronize(blockDisconnected.DisconnectedBlock.ChainedHeader))
                {
                    this.PollsRepository.WithTransaction(transaction =>
                    {
                        this.UnProcessBlock(transaction, blockDisconnected.DisconnectedBlock);
                        transaction.Commit();
                    });
                }
            });
        }

        [NoTrace]
        private void AddComponentStats(StringBuilder log)
        {
            log.AppendLine();
            log.AppendLine(">> Voting & Poll Data");

            lock (this.locker)
            {
                double avgBlockProcessingTime;
                if (this.blocksProcessed == 0)
                    avgBlockProcessingTime = double.NaN;
                else
                    avgBlockProcessingTime = Math.Round((double)(new TimeSpan(this.blocksProcessingTime).Milliseconds) / this.blocksProcessed, 2);

                double avgBlockProcessingThroughput = Math.Round(this.blocksProcessed / (new TimeSpan(this.blocksProcessingTime).TotalSeconds), 2);

                log.AppendLine("Polls Repository Height".PadRight(LoggingConfiguration.ColumnLength) + $": {(this.PollsRepository.CurrentTip?.Height ?? 0)}".PadRight(10) + $"(Hash: {(this.PollsRepository.CurrentTip?.Hash.ToString())})");
                log.AppendLine("Blocks Processed".PadRight(LoggingConfiguration.ColumnLength) + $": {this.blocksProcessed}".PadRight(18) + $"Avg Time: { avgBlockProcessingTime } ms".PadRight(20) + $"Throughput: { avgBlockProcessingThroughput } per second");
                log.AppendLine();

                log.AppendLine(
                    "Expired Member Polls".PadRight(24) + ": " + GetExpiredPolls().MemberPolls().Count.ToString().PadRight(16) +
                    "Expired Whitelist Polls".PadRight(30) + ": " + GetExpiredPolls().WhitelistPolls().Count);
                log.AppendLine(
                    "Pending Member Polls".PadRight(24) + ": " + GetPendingPolls().MemberPolls().Count.ToString().PadRight(16) +
                    "Pending Whitelist Polls".PadRight(30) + ": " + GetPendingPolls().WhitelistPolls().Count);
                log.AppendLine(
                    "Approved Member Polls".PadRight(24) + ": " + GetApprovedPolls().MemberPolls().Where(x => !x.IsExecuted).Count().ToString().PadRight(16) +
                    "Approved Whitelist Polls".PadRight(30) + ": " + GetApprovedPolls().WhitelistPolls().Where(x => !x.IsExecuted).Count());
                log.AppendLine(
                    "Executed Member Polls".PadRight(24) + ": " + GetExecutedPolls().MemberPolls().Count.ToString().PadRight(16) +
                    "Executed Whitelist Polls".PadRight(30) + ": " + GetExecutedPolls().WhitelistPolls().Count);
                log.AppendLine(
                    "Scheduled Votes".PadRight(24) + ": " + this.scheduledVotingData.Count.ToString().PadRight(16) +
                    "Scheduled votes will be added to the next block this node mines.");

                if (this.nodeStats.DisplayBenchStats)
                {
                    long tipHeight = this.chainIndexer.Tip.Height;

                    List<Poll> pendingPolls = GetPendingPolls().OrderByDescending(p => p.PollStartBlockData.Height).ToList();
                    if (pendingPolls.Count != 0)
                    {
                        log.AppendLine();
                        log.AppendLine("--- Pending Add/Kick Member Polls ---");
                        foreach (Poll poll in pendingPolls.Where(p => !p.IsExecuted && (p.VotingData.Key == VoteKey.AddFederationMember || p.VotingData.Key == VoteKey.KickFederationMember)))
                        {
                            IFederationMember federationMember = ((PoAConsensusFactory)(this.network.Consensus.ConsensusFactory)).DeserializeFederationMember(poll.VotingData.Data);
                            string expiresIn = $", Expires In = {(Math.Max(this.poaConsensusOptions.Release1100ActivationHeight, poll.PollStartBlockData.Height + this.poaConsensusOptions.PollExpiryBlocks) - tipHeight)}";
                            log.Append($"{poll.VotingData.Key.ToString().PadLeft(22)}, PubKey = { federationMember.PubKey.ToHex() }, In Favor = {poll.PubKeysHexVotedInFavor.Count}{expiresIn}");
                            bool exists = this.federationManager.GetFederationMembers().Any(m => m.PubKey == federationMember.PubKey);
                            if (poll.VotingData.Key == VoteKey.AddFederationMember && exists)
                                log.Append(" (Already exists)");
                            if (poll.VotingData.Key == VoteKey.KickFederationMember && !exists)
                                log.Append(" (Does not exist)");
                            log.AppendLine();
                        }
                    }

                    List<Poll> approvedPolls = GetApprovedPolls().Where(p => !p.IsExecuted).OrderByDescending(p => p.PollVotedInFavorBlockData.Height).ToList();
                    if (approvedPolls.Count != 0)
                    {
                        log.AppendLine();
                        log.AppendLine("--- Approved Polls ---");
                        foreach (Poll poll in approvedPolls)
                        {
                            log.AppendLine($"{poll.VotingData.Key.ToString().PadLeft(22)}, Applied In = ({(poll.PollStartBlockData.Height - (tipHeight - this.network.Consensus.MaxReorgLength))})");
                        }
                    }
                }
            }

            log.AppendLine();
        }

        [NoTrace]
        private void EnsureInitialized()
        {
            if (!this.isInitialized)
            {
                throw new Exception("VotingManager is not initialized. Check that voting is enabled in PoAConsensusOptions.");
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.signals.Unsubscribe(this.blockConnectedSubscription);
            this.signals.Unsubscribe(this.blockDisconnectedSubscription);

            this.PollsRepository.Dispose();
        }
    }
}
