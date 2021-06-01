using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ConcurrentCollections;
using Microsoft.Extensions.Logging;
using NBitcoin;
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

        private IdleFederationMembersTracker idleFederationMembersTracker;

        private IdleFederationMembersTracker.Cursor idleFederationMembersCursor;

        private INodeLifetime nodeLifetime;

        private DBreezeSerializer dBreezeSerializer;

        /// <summary>In-memory collection of pending polls.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private List<Poll> polls;

        private SubscriptionToken blockConnectedSubscription;
        private SubscriptionToken blockDisconnectedSubscription;

        /// <summary>Collection of voting data that should be included in a block when it's mined.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private List<VotingData> scheduledVotingData;

        internal bool isInitialized;

        public VotingManager(IFederationManager federationManager, ILoggerFactory loggerFactory, IPollResultExecutor pollResultExecutor,
            INodeStats nodeStats, DataFolder dataFolder, DBreezeSerializer dBreezeSerializer, ISignals signals,
            Network network,
            IBlockRepository blockRepository = null,
            ChainIndexer chainIndexer = null,
            INodeLifetime nodeLifetime = null,
            NodeSettings nodeSettings = null)
        {
            this.federationManager = Guard.NotNull(federationManager, nameof(federationManager));
            this.pollResultExecutor = Guard.NotNull(pollResultExecutor, nameof(pollResultExecutor));
            this.signals = Guard.NotNull(signals, nameof(signals));
            this.nodeStats = Guard.NotNull(nodeStats, nameof(nodeStats));

            this.locker = new object();
            this.votingDataEncoder = new VotingDataEncoder(loggerFactory);
            this.scheduledVotingData = new List<VotingData>();
            this.PollsRepository = new PollsRepository(dataFolder, loggerFactory, dBreezeSerializer, chainIndexer, nodeSettings);

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.poaConsensusOptions = (PoAConsensusOptions)this.network.Consensus.Options;

            this.blockRepository = blockRepository;
            this.chainIndexer = chainIndexer;
            this.nodeLifetime = nodeLifetime;
            this.dBreezeSerializer = dBreezeSerializer;
        }

        public void Initialize(IFederationHistory federationHistory)
        {
            this.federationHistory = federationHistory;
            this.idleFederationMembersTracker = new IdleFederationMembersTracker(this.network, this.PollsRepository, this.dBreezeSerializer, this.chainIndexer, federationHistory);
            this.idleFederationMembersCursor = new IdleFederationMembersTracker.Cursor(this.idleFederationMembersTracker);

            this.PollsRepository.Initialize();
            this.idleFederationMembersTracker.Initialize();

            this.PollsRepository.WithTransaction(transaction => this.polls = this.PollsRepository.GetAllPolls(transaction));

            this.blockConnectedSubscription = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);
            this.blockDisconnectedSubscription = this.signals.Subscribe<BlockDisconnected>(this.OnBlockDisconnected);

            this.nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name, 1200);

            this.isInitialized = true;

            this.PollsRepository.Synchronous(() =>
            {
                this.Synchronize(this.chainIndexer.Tip);
            });

            this.logger.LogDebug("VotingManager initialized.");
        }

        /// <summary> Remove all polls that started on or after the given height.</summary>
        /// <param name="height">The height to clean polls from.</param>
        public void DeletePollsAfterHeight(int height)
        {
            this.PollsRepository.WithTransaction(transaction =>
            {
                this.logger.LogInformation($"Cleaning poll data from height {height}.");

                var idsToRemove = new List<int>();

                this.polls = this.PollsRepository.GetAllPolls(transaction);

                foreach (Poll poll in this.polls.Where(p => p.PollStartBlockData.Height >= height))
                {
                    idsToRemove.Add(poll.Id);
                }

                if (idsToRemove.Any())
                {
                    this.PollsRepository.DeletePollsAndSetHighestPollId(transaction, idsToRemove.ToArray());
                    this.polls = this.PollsRepository.GetAllPolls(transaction);
                    transaction.Commit();
                }
            });
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

        public bool CanGetFederationForBlock(ChainedHeader chainedHeader)
        {
            return (chainedHeader.Height < ((this.PollsRepository.CurrentTip?.Height ?? 0) + this.network.Consensus.MaxReorgLength));
        }

        private Dictionary<uint256, List<IFederationMember>> cachedFederations = new Dictionary<uint256, List<IFederationMember>>();

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
                    modifiedFederation[i] = new CollateralFederationMember(member.PubKey,
                        shouldBeMultisigMember, member.CollateralAmount, member.CollateralMainchainAddress)
                    { JoinedTime = member.JoinedTime };
                }
            }
        }

        public List<IFederationMember> GetModifiedFederation(ChainedHeader chainedHeader)
        {
            lock (this.locker)
            {
                if (this.cachedFederations.TryGetValue(chainedHeader.HashBlock, out List<IFederationMember> modifiedFederation))
                    return new List<IFederationMember>(modifiedFederation);

                // It's not possible to determine the federation reliably if the polls repository is too far behind.
                if (((this.PollsRepository.CurrentTip?.Height ?? 0) + this.network.Consensus.MaxReorgLength) <= chainedHeader.Height)
                {
                    this.logger.LogWarning("The polls repository is too far behind to reliably determine the federation members.");
                }

                // Starting with the genesis federation...
                modifiedFederation = new List<IFederationMember>(this.poaConsensusOptions.GenesisFederationMembers);

                // Modify the federation with the polls that would have been executed up to the given height.
                if (this.network.Consensus.ConsensusFactory is PoAConsensusFactory poaConsensusFactory)
                {
                    bool straxEra = false;
                    int? multisigMinersApplicabilityHeight = this.federationManager.GetMultisigMinersApplicabilityHeight();
                    List<Poll> approvedPolls = this.GetApprovedPolls().MemberPolls().OrderBy(a => a.PollVotedInFavorBlockData.Height).ToList();

                    foreach (Poll poll in approvedPolls)
                    {
                        // When block "PollVotedInFavorBlockData"+MaxReorgLength connects, block "PollVotedInFavorBlockData" is executed. See VotingManager.OnBlockConnected.
                        int pollExecutionHeight = poll.PollVotedInFavorBlockData.Height + (int)this.network.Consensus.MaxReorgLength;
                        if (pollExecutionHeight > chainedHeader.Height)
                            break;

                        if (!straxEra && (multisigMinersApplicabilityHeight != null && pollExecutionHeight >= multisigMinersApplicabilityHeight))
                        {
                            EnterStraxEra(modifiedFederation);
                            straxEra = true;
                        }

                        IFederationMember federationMember = ((PoAConsensusFactory)(this.network.Consensus.ConsensusFactory)).DeserializeFederationMember(poll.VotingData.Data);

                        // Addition/removal.
                        if (poll.VotingData.Key == VoteKey.AddFederationMember)
                        {
                            if (!modifiedFederation.Contains(federationMember))
                            {
                                ChainedHeader joinedHeader = chainedHeader.GetAncestor(pollExecutionHeight);
                                federationMember.JoinedTime = (new HashHeightPair(joinedHeader), joinedHeader.Header.Time);
                                if (straxEra && federationMember is CollateralFederationMember collateralFederationMember)
                                {
                                    bool shouldBeMultisigMember = ((PoANetwork)this.network).StraxMiningMultisigMembers.Contains(federationMember.PubKey);
                                    if (collateralFederationMember.IsMultisigMember != shouldBeMultisigMember)
                                        collateralFederationMember.IsMultisigMember = shouldBeMultisigMember;
                                }
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

                    if (!straxEra && (multisigMinersApplicabilityHeight != null && chainedHeader.Height >= multisigMinersApplicabilityHeight))
                        EnterStraxEra(modifiedFederation);
                }

                this.cachedFederations[chainedHeader.HashBlock] = modifiedFederation;

                return modifiedFederation;
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

        private void ProcessBlock(DBreeze.Transactions.Transaction transaction, ChainedHeaderBlock chBlock, List<IFederationMember> modifiedFederation)
        {
            try
            {
                int? multisigMinersApplicabilityHeight = this.federationManager.GetMultisigMinersApplicabilityHeight();
                if (chBlock.ChainedHeader.Height == multisigMinersApplicabilityHeight)
                    this.EnterStraxEra(modifiedFederation);

                lock (this.locker)
                {
                    this.PollsRepository.SaveCurrentTip(null, chBlock.ChainedHeader);

                    foreach (Poll poll in this.GetApprovedPolls())
                    {
                        if (chBlock.ChainedHeader.Height != (poll.PollVotedInFavorBlockData.Height + this.network.Consensus.MaxReorgLength))
                            continue;

                        this.logger.LogDebug("Applying poll '{0}'.", poll);
                        this.pollResultExecutor.ApplyChange(poll.VotingData, modifiedFederation);

                        poll.PollExecutedBlockData = new HashHeightPair(chBlock.ChainedHeader);
                        this.PollsRepository.UpdatePoll(transaction, poll);

                        if (poll.VotingData.Key == VoteKey.AddFederationMember)
                        {
                            IFederationMember federationMember = ((PoAConsensusFactory)this.network.Consensus.ConsensusFactory).DeserializeFederationMember(poll.VotingData.Data);
                            this.idleFederationMembersCursor.RecordActivity(transaction, federationMember.PubKey, chBlock.ChainedHeader, IdleFederationMembersTracker.Activity.Joined, chBlock.ChainedHeader.Header.Time);
                        }
                    }
                }

                IFederationMember member = this.federationHistory.GetFederationMemberForBlock(chBlock.ChainedHeader, modifiedFederation);

                PubKey fedMemberKey = member.PubKey;

                this.idleFederationMembersCursor.RecordActivity(transaction, fedMemberKey, chBlock.ChainedHeader, IdleFederationMembersTracker.Activity.Mined, chBlock.ChainedHeader.Header.Time);

                byte[] rawVotingData = this.votingDataEncoder.ExtractRawVotingData(chBlock.Block.Transactions[0]);

                if (rawVotingData == null)
                {
                    this.logger.LogTrace("(-)[NO_VOTING_DATA]");
                    return;
                }

                string fedMemberKeyHex = fedMemberKey.ToHex();

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

                        Poll poll = this.polls.SingleOrDefault(x => x.VotingData == data && !x.IsExecuted);

                        Guard.Assert(poll == null || poll.PollStartBlockData.Height < chBlock.ChainedHeader.Height);

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
                                    PubKeysHexVotedInFavor = new List<string>() { fedMemberKeyHex }
                                };

                                this.polls.Add(poll);
                                this.PollsRepository.AddPolls(transaction, poll);

                                this.logger.LogDebug("New poll was created: '{0}'.", poll);
                            });
                        }
                        else if (poll.IsPending && !poll.PubKeysHexVotedInFavor.Contains(fedMemberKeyHex))
                        {
                            poll.PubKeysHexVotedInFavor.Add(fedMemberKeyHex);
                            this.PollsRepository.UpdatePoll(transaction, poll);

                            this.logger.LogDebug("Voted on existing poll: '{0}'.", poll);
                        }
                        else
                        {
                            this.logger.LogDebug("Fed member '{0}' already voted for this poll. Ignoring his vote. Poll: '{1}'.", fedMemberKeyHex, poll);
                        }

                        ChainedHeader chainedHeader = chBlock.ChainedHeader.GetAncestor(poll.PollStartBlockData.Height);

                        if (chainedHeader?.Header == null)
                        {
                            this.logger.LogWarning("Couldn't retrieve header for block at height-hash: {0}-{1}.", poll.PollStartBlockData.Height, poll.PollStartBlockData.Hash?.ToString());

                            Guard.NotNull(chainedHeader, nameof(chainedHeader));
                            Guard.NotNull(chainedHeader.Header, nameof(chainedHeader.Header));
                        }


                        // Inactive members don't participate in voting.
                        ChainedHeader pollStartHeader = this.chainIndexer.GetHeader(poll.PollStartBlockData.Hash);
                        var voters = new ConcurrentHashSet<string>(modifiedFederation
                            .Where(m => ((CollateralFederationMember)m).IsMultisigMember || !this.idleFederationMembersCursor.IsMemberInactive(transaction, m, chBlock.ChainedHeader))
                            .Select(m => m.PubKey.ToHex()))
                            .ToArray();

                        // It is possible that there is a vote from a federation member that was deleted from the federation.
                        int validVotesCount = poll.PubKeysHexVotedInFavor.Count(x => voters.Contains(x));
                        int requiredVotesCount = (voters.Length / 2) + 1;

                        if (poll.VotingData.Key == VoteKey.AddFederationMember)
                        {
                            string memberVotedOn = ((PoAConsensusFactory)this.network.Consensus.ConsensusFactory).DeserializeFederationMember(poll.VotingData.Data).PubKey.ToHex();
                            if (memberVotedOn == "02127bcb04dc37dcca35ba58c03453607ebc70183ca6ea792cec73e5e64c962224")
                                if (validVotesCount >= 27) /* > 1476640 */
                                    requiredVotesCount = 27;
                        }

                        this.logger.LogDebug("Fed members count: {0}, valid votes count: {1}, required votes count: {2}.", modifiedFederation.Count, validVotesCount, requiredVotesCount);

                        if (validVotesCount < requiredVotesCount)
                            continue;

                        poll.PollVotedInFavorBlockData = new HashHeightPair(chBlock.ChainedHeader);
                        this.PollsRepository.UpdatePoll(transaction, poll);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, ex.ToString());
                throw;
            }
        }

        private void UnProcessBlock(DBreeze.Transactions.Transaction transaction, ChainedHeaderBlock chBlock, List<IFederationMember> modifiedFederation)
        {
            lock (this.locker)
            {
                foreach (Poll poll in this.polls.Where(x => !x.IsPending && x.PollExecutedBlockData?.Hash == chBlock.ChainedHeader.HashBlock).ToList())
                {
                    this.logger.LogDebug("Reverting poll execution '{0}'.", poll);
                    this.pollResultExecutor.RevertChange(poll.VotingData, modifiedFederation);

                    poll.PollExecutedBlockData = null;
                    this.PollsRepository.UpdatePoll(transaction, poll);
                }
            }

            byte[] rawVotingData = this.votingDataEncoder.ExtractRawVotingData(chBlock.Block.Transactions[0]);

            if (rawVotingData == null)
            {
                this.logger.LogTrace("(-)[NO_VOTING_DATA]");

                this.PollsRepository.SaveCurrentTip(null, chBlock.ChainedHeader.Previous);
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

                        this.PollsRepository.UpdatePoll(transaction, targetPoll);
                    }

                    // Pub key of a fed member that created voting data.
                    string fedMemberKeyHex = this.federationHistory.GetFederationMemberForBlock(chBlock.ChainedHeader).PubKey.ToHex();

                    targetPoll.PubKeysHexVotedInFavor.Remove(fedMemberKeyHex);

                    if (targetPoll.PubKeysHexVotedInFavor.Count == 0)
                    {
                        this.polls.Remove(targetPoll);
                        this.PollsRepository.RemovePolls(transaction, targetPoll.Id);

                        this.logger.LogDebug("Poll with Id {0} was removed.", targetPoll.Id);
                    }
                }

                this.PollsRepository.SaveCurrentTip(null, chBlock.ChainedHeader.Previous);
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

        private bool Synchronize(ChainedHeader newTip)
        {
            Guard.Assert(this.blockRepository != null);

            ChainedHeader repoTip = GetPollsRepositoryTip();
            if (repoTip == newTip)
                return true;

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

                                this.UnProcessBlock(transaction, new ChainedHeaderBlock(block, header), modifiedFederation);
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
                    List<IFederationMember> modifiedFederation = this.GetFederationAtPollsRepositoryTip(repoTip);

                    this.PollsRepository.WithTransaction(transaction =>
                    {
                        int i = 0;
                        foreach (Block block in this.blockRepository.EnumerateBatch(headers))
                        {
                            if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                            {
                                this.logger.LogTrace("(-)[NODE_DISPOSED]");
                                transaction.Commit();

                                bSuccess = false;
                                return;
                            }

                            ChainedHeader header = headers[i++];
                            this.ProcessBlock(transaction, new ChainedHeaderBlock(block, header), modifiedFederation);

                            if (header.Height % 1000 == 0)
                            {
                                this.logger.LogInformation($"Synchronizing voting data at height {header.Height}.");
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
                        List<IFederationMember> modifiedFederation = this.GetFederationAtPollsRepositoryTip(this.GetPollsRepositoryTip());
                        this.ProcessBlock(transaction, blockConnected.ConnectedBlock, modifiedFederation);
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
                        List<IFederationMember> modifiedFederation = this.GetFederationAtPollsRepositoryTip(this.GetPollsRepositoryTip());
                        this.UnProcessBlock(transaction, blockDisconnected.DisconnectedBlock, modifiedFederation);
                        transaction.Commit();
                    });
                }
            });
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
