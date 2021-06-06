using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Persistence;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public interface IIdleFederationMembersKicker : IDisposable
    {
        /// <summary>
        /// Determines when a federation member was last active. This includes mining or joining.
        /// </summary>
        /// <param name="federationMember">Member to check activity of.</param>
        /// <param name="blockHeader">Block at which to check for past activity.</param>
        /// <param name="lastActiveTime">Time at which member was last active.</param>
        /// <returns><c>True</c> if the information is available and <c>false</c> otherwise.</returns>
        bool GetLastActiveTime(IFederationMember federationMember, ChainedHeader blockHeader, out uint lastActiveTime);

        /// <summary>
        /// Determines when the current federation members were last active. This includes mining or joining.
        /// </summary>
        /// <returns>A list of public keys and the times at which they were active.</returns>
        ConcurrentDictionary<IFederationMember, uint> GetFederationMembersByLastActiveTime();

        /// <summary>
        /// Determines if a federation member should be kicked.
        /// </summary>
        /// <param name="federationMember">Member to check activity of.</param>
        /// <param name="chainedHeader">Block at which to check for past activity.</param>
        /// <param name="consensusTip">Typically the poll repository tip.</param>
        /// <param name="inactiveForSeconds">Number of seconds member was inactive for.</param>
        /// <returns><c>True</c> if the member should be kicked and <c>false</c> otherwise.</returns>
        bool ShouldMemberBeKicked(IFederationMember federationMember, ChainedHeader chainedHeader, ChainedHeader consensusTip, out uint inactiveForSeconds);

        /// <summary>
        /// Determine whether or not any miners should be scheduled to be kicked from the federation at the current tip.
        /// </summary>
        /// <param name="consensusTip">The current consenus tip.</param>
        void Execute(ChainedHeader consensusTip);

        /// <summary>
        /// Initializes this component.
        /// </summary>
        void Initialize();

        /// <summary> 
        /// Clears the recorded member activity before a re-sync.
        /// </summary>
        void ResetFederationMemberLastActiveTime();

        bool SaveStatePeriodically { get; set; }
    }

    /// <summary>
    /// Automatically schedules addition of voting data that votes for kicking federation member that
    /// didn't produce a block in <see cref="PoAConsensusOptions.FederationMemberMaxIdleTimeSeconds"/>.
    /// </summary>
    public class IdleFederationMembersKicker : IIdleFederationMembersKicker
    {
        private readonly IKeyValueRepository keyValueRepository;

        private readonly IConsensusManager consensusManager;

        private readonly IAsyncProvider asyncProvider;

        private readonly Network network;

        private readonly IFederationManager federationManager;

        private readonly VotingManager votingManager;

        private readonly IFederationHistory federationHistory;

        private readonly ILogger logger;

        private readonly ChainIndexer chainIndexer;

        private readonly uint federationMemberMaxIdleTimeSeconds;

        private readonly PoAConsensusFactory consensusFactory;

        private IAsyncLoop asyncLoop;

        private readonly INodeLifetime nodeLifetime;

        private readonly object lockObject;

        public ConcurrentDictionary<PubKey, List<uint>> lastActiveTimes;
        public ChainedHeader lastActiveTip;

        public bool SaveStatePeriodically { get; set; }

        private const string fedMembersByLastActiveTimeKey = "fedMembersByLastActiveTime";
        private const string lastActiveTipKey = "lastActiveTip";

        public IdleFederationMembersKicker(Network network, IKeyValueRepository keyValueRepository, IConsensusManager consensusManager, IAsyncProvider asyncProvider, INodeLifetime nodeLifetime,
            IFederationManager federationManager, VotingManager votingManager, IFederationHistory federationHistory, ILoggerFactory loggerFactory, ChainIndexer chainIndexer)
        {
            this.network = network;
            this.keyValueRepository = keyValueRepository;
            this.consensusManager = consensusManager;
            this.asyncProvider = asyncProvider;
            this.nodeLifetime = nodeLifetime;
            this.federationManager = federationManager;
            this.votingManager = votingManager;
            this.federationHistory = federationHistory;

            this.consensusFactory = this.network.Consensus.ConsensusFactory as PoAConsensusFactory;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.federationMemberMaxIdleTimeSeconds = ((PoAConsensusOptions)network.Consensus.Options).FederationMemberMaxIdleTimeSeconds;
            this.chainIndexer = chainIndexer;
            this.lockObject = new object();
        }

        /// <inheritdoc />
        public void Initialize()
        {
            lock (this.lockObject)
            {
                ResetFederationMemberLastActiveTime();

                this.SaveStatePeriodically = true;

                this.asyncLoop = this.asyncProvider.CreateAndRunAsyncLoop($"{this.GetType().Name}.{nameof(PeriodicSaveAsync)}", async token =>
                {
                    await PeriodicSaveAsync().ConfigureAwait(false);
                }, 
                this.nodeLifetime.ApplicationStopping,
                repeatEvery: TimeSpan.FromSeconds(30));

                Dictionary<string, uint> loaded = this.keyValueRepository.LoadValueJson<Dictionary<string, uint>>(fedMembersByLastActiveTimeKey);
                if (loaded != null)
                {
                    foreach (KeyValuePair<string, uint> loadedMember in loaded)
                    {
                        PubKey pubKey = new PubKey(loadedMember.Key);
                        if (!this.lastActiveTimes.TryGetValue(pubKey, out List<uint> activity))
                        {
                            activity = new List<uint>();
                            this.lastActiveTimes[pubKey] = activity;
                        }

                        activity.Add(loadedMember.Value);
                    }
                }

                HashHeightPair lastActiveTip = this.keyValueRepository.LoadValue<HashHeightPair>(lastActiveTipKey);
                if (lastActiveTip != null)
                {
                    this.lastActiveTip = this.chainIndexer.GetHeader(lastActiveTip.Hash);
                    return;
                }

                if (this.lastActiveTimes.Count == 0)
                    return;

                // Try to determine the tip if none could be loaded.
                uint maxTime = 0;

                foreach ((PubKey pubKey, List<uint> activity) in this.lastActiveTimes)
                {
                    if (activity.LastOrDefault() > maxTime)
                        maxTime = activity.LastOrDefault();
                }

                if (this.chainIndexer.Tip.Header.Time < maxTime)
                {
                    DiscardActivityAboveTime(this.chainIndexer.Tip.Header.Time);
                    this.lastActiveTip = this.chainIndexer.Tip;
                    return;
                }

                int height = BinarySearch.BinaryFindFirst(x => this.chainIndexer.GetHeader(x).Header.Time >= maxTime, 0, this.chainIndexer.Tip.Height + 1);

                this.lastActiveTip = this.chainIndexer.GetHeader(height);
            }
        }

        /// <inheritdoc />
        public void ResetFederationMemberLastActiveTime()
        {
            lock (this.lockObject)
            {
                this.lastActiveTip = null;
                this.lastActiveTimes = new ConcurrentDictionary<PubKey, List<uint>>();
            }
        }

        /// <inheritdoc />
        public ConcurrentDictionary<IFederationMember, uint> GetFederationMembersByLastActiveTime()
        {
            lock (this.lockObject)
            {
                ChainedHeader tip = this.lastActiveTip ?? this.chainIndexer.GetHeader(0);

                List<IFederationMember> federationMembers = this.federationHistory.GetFederationForBlock(tip);

                return new ConcurrentDictionary<IFederationMember, uint>(federationMembers
                    .Select(m => (m, (this.GetLastActiveTime(m, tip, out uint lastActiveTime) && lastActiveTime != default) ? lastActiveTime : this.network.GenesisTime))
                    .ToDictionary(x => x.m, x => x.Item2));
            }
        }

        private void DiscardActivityAboveTime(uint discardAboveTime)
        {
            var remove = new List<PubKey>();

            foreach ((PubKey pubKey, List<uint> activity) in this.lastActiveTimes)
            {
                int pos = BinarySearch.BinaryFindFirst(x => x > discardAboveTime, 0, activity.Count);
                if (pos >= 0)
                {
                    if (pos == 0)
                        remove.Add(pubKey);
                    else
                        activity.RemoveRange(pos, activity.Count - pos);
                }
            }

            foreach (PubKey pubKey in remove)
                this.lastActiveTimes.Remove(pubKey, out _);
        }

        private void UpdateTip(ChainedHeader blockHeader)
        {
            if (this.lastActiveTip != null)
            {
                if (blockHeader == this.lastActiveTip)
                    return;

                ChainedHeader fork = this.lastActiveTip.FindFork(blockHeader);

                // If the current chain includes the block then do nothing.
                if (fork == blockHeader)
                    return;

                // If the fork shows blocks that are not in common then discard those blocks.
                if (fork != this.lastActiveTip)
                {
                    DiscardActivityAboveTime(fork.Header.Time);
                    this.lastActiveTip = fork;
                }
            }

            var federationMemberActivationTime = ((PoAConsensusOptions)this.network.Consensus.Options).FederationMemberActivationTime ?? 0;

            ChainedHeader[] headers = blockHeader
                .EnumerateToGenesis()
                .TakeWhile(h => h.HashBlock != this.lastActiveTip?.HashBlock && h.Header.Time >= federationMemberActivationTime)
                .Reverse().ToArray();

            if (headers.Length != 0)
            {
                (IFederationMember[] miners, (List<IFederationMember> members, HashSet<IFederationMember> whoJoined)[] federations) = this.federationHistory.GetFederationMembersForBlocks(headers);

                for (int i = 0; i < headers.Length; i++)
                {
                    ChainedHeader header = headers[i];

                    uint headerTime = header.Header.Time;

                    if (miners[i] != null)
                    {
                        if (!this.lastActiveTimes.TryGetValue(miners[i].PubKey, out List<uint> minerActivity))
                        {
                            minerActivity = new List<uint>();
                            this.lastActiveTimes[miners[i].PubKey] = minerActivity;
                        }

                        if (minerActivity.LastOrDefault() != headerTime)
                            minerActivity.Add(headerTime);
                    }

                    foreach (IFederationMember member in federations[i].whoJoined)
                    {
                        if (!this.lastActiveTimes.TryGetValue(member.PubKey, out List<uint> joinActivity))
                        {
                            joinActivity = new List<uint>();
                            this.lastActiveTimes[member.PubKey] = joinActivity;
                        }

                        if (joinActivity.LastOrDefault() != headerTime)
                            joinActivity.Add(headerTime);
                    }
                }
            }

            this.lastActiveTip = blockHeader;
        }

        /// <inheritdoc />
        public bool GetLastActiveTime(IFederationMember federationMember, ChainedHeader blockHeader, out uint lastActiveTime)
        {
            lock (this.lockObject)
            {
                UpdateTip(blockHeader);

                if (this.lastActiveTimes.TryGetValue(federationMember.PubKey, out List<uint> activity))
                {
                    uint blockTime = blockHeader.Header.Time;

                    lastActiveTime = activity.Last();
                    if (activity.Last() <= blockTime)
                        return true;

                    int pos = BinarySearch.BinaryFindFirst(i => activity[i] > blockTime, 0, activity.Count);

                    if (pos > 0)
                    {
                        lastActiveTime = activity[pos - 1];
                        return true;
                    }
                }

                if (((PoAConsensusOptions)this.network.Consensus.Options).GenesisFederationMembers.Any(m => m.PubKey == federationMember.PubKey))
                {
                    lastActiveTime = default;
                    return true;
                }

                // This should never happen.
                this.logger.LogWarning("Could not resolve federation member's first activity.");

                lastActiveTime = default;
                return false;
            }
        }

        /// <inheritdoc />
        public bool ShouldMemberBeKicked(IFederationMember federationMember, ChainedHeader blockHeader, ChainedHeader currentTip, out uint inactiveForSeconds)
        {
            Guard.NotNull(federationMember, nameof(federationMember));

            lock (this.lockObject)
            {
                PubKey pubKey = federationMember.PubKey;

                if (this.lastActiveTimes == null)
                    throw new Exception($"'{nameof(IdleFederationMembersKicker)}' has not been initialized.");

                uint lastActiveTime = this.network.GenesisTime;
                if (this.GetLastActiveTime(federationMember, currentTip, out lastActiveTime))
                    lastActiveTime = (lastActiveTime != default) ? lastActiveTime : this.network.GenesisTime;

                uint blockTime = blockHeader.Header.Time;

                inactiveForSeconds = blockTime - lastActiveTime;

                // This might happen in test setup scenarios.
                if (blockTime < lastActiveTime)
                    inactiveForSeconds = 0;

                return inactiveForSeconds > this.federationMemberMaxIdleTimeSeconds && !(federationMember is CollateralFederationMember collateralFederationMember && collateralFederationMember.IsMultisigMember);
            }
        }

        /// <inheritdoc />
        public void Execute(ChainedHeader consensusTip)
        {
            lock (this.lockObject)
            {
                // No member can be kicked at genesis.
                if (consensusTip.Height == 0)
                    return;

                // Federation member kicking is not yet enabled.
                var federationMemberActivationTime = ((PoAConsensusOptions)this.network.Consensus.Options).FederationMemberActivationTime;
                if (federationMemberActivationTime != null &&
                    federationMemberActivationTime > 0 &&
                    consensusTip.Header.Time < federationMemberActivationTime)
                    return;

                try
                {
                    UpdateTip(consensusTip);

                    // Check if any fed member was idle for too long. Use the timestamp of the mined block.
                    foreach ((IFederationMember federationMember, uint lastActiveTime) in this.GetFederationMembersByLastActiveTime())
                    {
                        if (this.ShouldMemberBeKicked(federationMember, consensusTip, consensusTip, out uint inactiveForSeconds))
                        {
                            byte[] federationMemberBytes = this.consensusFactory.SerializeFederationMember(federationMember);

                            bool alreadyKicking = this.votingManager.AlreadyVotingFor(VoteKey.KickFederationMember, federationMemberBytes);

                            if (!alreadyKicking)
                            {
                                this.logger.LogWarning("Federation member '{0}' was inactive for {1} seconds and will be scheduled to be kicked.", federationMember.PubKey, inactiveForSeconds);

                                this.votingManager.ScheduleVote(new VotingData()
                                {
                                    Key = VoteKey.KickFederationMember,
                                    Data = federationMemberBytes
                                });
                            }
                            else
                            {
                                this.logger.LogDebug("Skipping because kicking is already voted for.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, ex.ToString());
                    throw;
                }
            }
        }

        /// <inheritdoc />
        private void SaveMembersByLastActiveTime()
        {
            if (this.lastActiveTip != null)
            {
                var members = this.GetFederationMembersByLastActiveTime();
                var dataToSave = new Dictionary<string, uint>();

                foreach (KeyValuePair<IFederationMember, uint> pair in members)
                    dataToSave.Add(pair.Key.PubKey.ToHex(), pair.Value);

                this.keyValueRepository.SaveValueJson(fedMembersByLastActiveTimeKey, dataToSave);
                this.keyValueRepository.SaveValueJson(lastActiveTipKey, new HashHeightPair(this.lastActiveTip));
            }
        }

        public async Task PeriodicSaveAsync()
        {
            lock (this.lockObject)
            {
                if (this.SaveStatePeriodically && this.votingManager.IsInitialized)
                    this.SaveMembersByLastActiveTime();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
