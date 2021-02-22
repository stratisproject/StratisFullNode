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

        private bool isInitialized;
        private bool isBusyReconstructing;

        public VotingManager(IFederationManager federationManager, ILoggerFactory loggerFactory, IPollResultExecutor pollResultExecutor,
            INodeStats nodeStats, DataFolder dataFolder, DBreezeSerializer dBreezeSerializer, ISignals signals,
            IFinalizedBlockInfoRepository finalizedBlockInfo,
            Network network,
            IBlockRepository blockRepository = null,
            ChainIndexer chainIndexer = null)
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

            this.isInitialized = false;
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

            this.isInitialized = true;

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

        /// <summary> Reconstructs voting and poll data from a given height.</summary>
        /// <param name="height">The height to start reconstructing from.</param>
        public void ReconstructVotingDataFromHeightLocked(int height)
        {
            try
            {
                this.isBusyReconstructing = true;

                var currentHeight = height;
                var progress = $"Reconstructing voting poll data from height {currentHeight}.";
                this.logger.LogInformation(progress);
                this.signals.Publish(new RecontructFederationProgressEvent() { Progress = progress });

                do
                {
                    ChainedHeader chainedHeader = this.chainIndexer.GetHeader(currentHeight);
                    if (chainedHeader == null)
                        break;

                    Block block = this.blockRepository.GetBlock(chainedHeader.HashBlock);
                    if (block == null)
                        break;

                    var chainedHeaderBlock = new ChainedHeaderBlock(block, chainedHeader);

                    this.idleFederationMembersKicker.UpdateFederationMembersLastActiveTime(chainedHeaderBlock, false);

                    OnBlockConnected(new BlockConnected(chainedHeaderBlock));

                    currentHeight++;

                    if (currentHeight % 10000 == 0)
                    {
                        progress = $"Reconstructing voting data at height {currentHeight}";
                        this.logger.LogInformation(progress);
                        this.signals.Publish(new RecontructFederationProgressEvent() { Progress = progress });
                    }
                } while (true);
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

        public List<IFederationMember> GetModifiedFederation(ChainedHeader chainedHeader)
        {
            lock (this.locker)
            {
                // Starting with the genesis federation...
                var modifiedFederation = new List<IFederationMember>(this.poaConsensusOptions.GenesisFederationMembers);
                IEnumerable<Poll> approvedPolls = this.GetApprovedPolls().MemberPolls();

                // Modify the federation with the polls that would have been executed up to the given height.
                if (this.network.Consensus.ConsensusFactory is PoAConsensusFactory poaConsensusFactory)
                {
                    foreach (Poll poll in approvedPolls.OrderBy(a => a.PollVotedInFavorBlockData.Height))
                    {
                        // When block "PollVotedInFavorBlockData"+MaxReorgLength connects, block "PollVotedInFavorBlockData" is executed. See VotingManager.OnBlockConnected.
                        if ((poll.PollVotedInFavorBlockData.Height + this.network.Consensus.MaxReorgLength) > chainedHeader.Height)
                            break;

                        IFederationMember federationMember = ((PoAConsensusFactory)(this.network.Consensus.ConsensusFactory)).DeserializeFederationMember(poll.VotingData.Data);

                        // Addition/removal.
                        if (poll.VotingData.Key == VoteKey.AddFederationMember)
                            modifiedFederation.Add(federationMember);
                        else if (poll.VotingData.Key == VoteKey.KickFederationMember)
                            modifiedFederation.Remove(federationMember);
                    }

                    // Set the IsMultisigMember flags to match the expected values.
                    int? multisigMinersApplicabilityHeight = this.federationManager.GetMultisigMinersApplicabilityHeight();
                    if (multisigMinersApplicabilityHeight != null && chainedHeader.Height < multisigMinersApplicabilityHeight)
                    {
                        // If we are accessing blocks prior to STRAX activation then the IsMultisigMember values for the members may be different. 
                        foreach (CollateralFederationMember member in modifiedFederation.Where(m => m is CollateralFederationMember))
                        {
                            bool wasMultisigMember = ((PoAConsensusOptions)this.network.Consensus.Options).GenesisFederationMembers
                                .Any(m => m.PubKey == member.PubKey && ((CollateralFederationMember)m).IsMultisigMember);

                            if (member.IsMultisigMember != wasMultisigMember)
                            {
                                // Clone the member if we will be changing the flag.
                                modifiedFederation[modifiedFederation.IndexOf(member)] = new CollateralFederationMember(member.PubKey,
                                    wasMultisigMember, member.CollateralAmount, member.CollateralMainchainAddress);
                            }
                        }
                    }
                }

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

                string fedMemberKeyHex;

                // Please see the description under `VotingManagerV2ActivationHeight`.
                // PubKey of the federation member that created the voting data.
                if (this.poaConsensusOptions.VotingManagerV2ActivationHeight == 0 || blockConnected.ConnectedBlock.ChainedHeader.Height < this.poaConsensusOptions.VotingManagerV2ActivationHeight)
                    fedMemberKeyHex = this.federationHistory.GetFederationMemberForTimestamp(chBlock.Block.Header.Time, this.poaConsensusOptions).PubKey.ToHex();
                else
                    fedMemberKeyHex = this.federationHistory.GetFederationMemberForBlock(chBlock.ChainedHeader).PubKey.ToHex();

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

                        var fedMembersHex = new ConcurrentHashSet<string>(this.federationManager.GetFederationMembers().Select(x => x.PubKey.ToHex()));

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

                            foreach (string pubKey in fedMembersHex)
                            {
                                if (this.idleFederationMembersKicker.ShouldMemberBeKicked(new PubKey(pubKey), chainedHeader.Header.Time, out _))
                                {
                                    fedMembersHex.TryRemove(pubKey);
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

            lock (this.locker)
            {
                log.AppendLine("Member Polls".PadRight(LoggingConfiguration.ColumnLength) + $": Pending: {GetPendingPolls().MemberPolls().Count} Approved: {GetApprovedPolls().MemberPolls().Count} Executed : {GetExecutedPolls().MemberPolls().Count}");
                log.AppendLine("Whitelist Polls".PadRight(LoggingConfiguration.ColumnLength) + $": Pending: {GetPendingPolls().WhitelistPolls().Count} Approved: {GetApprovedPolls().WhitelistPolls().Count} Executed : {GetExecutedPolls().WhitelistPolls().Count}");
                log.AppendLine("Scheduled Votes".PadRight(LoggingConfiguration.ColumnLength) + ": " + this.scheduledVotingData.Count);
                log.AppendLine("Scheduled votes will be added to the next block this node mines.");
                log.AppendLine();
            }
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

            this.pollsRepository.Dispose();
        }
    }
}
