using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
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
        /// <param name="lastActiveHeader">Block at which member was last active.</param>
        /// <returns><c>True</c> if the information is available and <c>false</c> otherwise.</returns>
        bool GetLastActiveTime(IFederationMember federationMember, ChainedHeader blockHeader, out ChainedHeader lastActiveHeader);

        /// <summary>
        /// Determines when the current federation members were last active. This includes mining or joining.
        /// </summary>
        /// <returns>A list of public keys and the times at which they were active.</returns>
        ConcurrentDictionary<PubKey, uint> GetFederationMembersByLastActiveTime();

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

        /// <summary>
        /// Saves the current state.
        /// </summary>
        void SaveMembersByLastActiveTime();
    }

    /// <summary>
    /// Automatically schedules addition of voting data that votes for kicking federation member that
    /// didn't produce a block in <see cref="PoAConsensusOptions.FederationMemberMaxIdleTimeSeconds"/>.
    /// </summary>
    public class IdleFederationMembersKicker : IIdleFederationMembersKicker
    {
        private readonly IKeyValueRepository keyValueRepository;

        private readonly IConsensusManager consensusManager;

        private readonly Network network;

        private readonly IFederationManager federationManager;

        private readonly VotingManager votingManager;

        private readonly IFederationHistory federationHistory;

        private readonly ILogger logger;

        private readonly uint federationMemberMaxIdleTimeSeconds;

        private readonly PoAConsensusFactory consensusFactory;

        // TODO: Use SortedSet? Can it be an ordinary list, assuming items can be added sequentially? How are re-orgs handled?
        public ConcurrentDictionary<PubKey, SortedList<uint, ChainedHeader>> lastActiveTimes;
        public ChainedHeader lastActiveTip; // Need to handle forks with this...

        private const string fedMembersByLastActiveTimeKey = "fedMembersByLastActiveTime";

        public IdleFederationMembersKicker(Network network, IKeyValueRepository keyValueRepository, IConsensusManager consensusManager,
            IFederationManager federationManager, VotingManager votingManager, IFederationHistory federationHistory, ILoggerFactory loggerFactory)
        {
            this.network = network;
            this.keyValueRepository = keyValueRepository;
            this.consensusManager = consensusManager;
            this.federationManager = federationManager;
            this.votingManager = votingManager;
            this.federationHistory = federationHistory;

            this.consensusFactory = this.network.Consensus.ConsensusFactory as PoAConsensusFactory;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.federationMemberMaxIdleTimeSeconds = ((PoAConsensusOptions)network.Consensus.Options).FederationMemberMaxIdleTimeSeconds;
        }

        /// <inheritdoc />
        public void Initialize()
        {
            ResetFederationMemberLastActiveTime();

            Dictionary<string, uint> loaded = this.keyValueRepository.LoadValueJson<Dictionary<string, uint>>(fedMembersByLastActiveTimeKey);
            if (loaded != null)
            {
                foreach (KeyValuePair<string, uint> loadedMember in loaded)
                {
                    PubKey pubKey = new PubKey(loadedMember.Key);
                    if (!this.lastActiveTimes.TryGetValue(pubKey, out SortedList<uint, ChainedHeader> activity))
                    {
                        activity = new SortedList<uint, ChainedHeader>();
                        this.lastActiveTimes[pubKey] = activity;
                    }

                    activity[loadedMember.Value] = null;
                }
            }
        }

        /// <inheritdoc />
        public void ResetFederationMemberLastActiveTime()
        {
            this.lastActiveTip = null;
            this.lastActiveTimes = new ConcurrentDictionary<PubKey, SortedList<uint, ChainedHeader>>();
        }

        /// <inheritdoc />
        public ConcurrentDictionary<PubKey, uint> GetFederationMembersByLastActiveTime()
        {
            List<IFederationMember> federationMembers = this.federationHistory.GetFederationForBlock(this.lastActiveTip);

            return new ConcurrentDictionary<PubKey, uint>(federationMembers
                .Select(m => (m.PubKey, (this.GetLastActiveTime(m, this.lastActiveTip, out ChainedHeader lastActiveHeader) && lastActiveHeader != null) ? lastActiveHeader.Header.Time : this.network.GenesisTime))
                .ToDictionary(x => x.PubKey, x => x.Item2));
        }

        public void UpdateTip(ChainedHeader blockHeader)
        {
            // TODO: Check for fork.

            if (blockHeader.Height <= (this.lastActiveTip?.Height ?? 0))
                return;

            var federationMemberActivationTime = ((PoAConsensusOptions)this.network.Consensus.Options).FederationMemberActivationTime;

            ChainedHeader[] headers = blockHeader
                .EnumerateToGenesis()
                .TakeWhile(h => h.HashBlock != this.lastActiveTip?.HashBlock && h.Header.Time >= federationMemberActivationTime)
                .Reverse().ToArray();

            if (headers.Length != 0)
            {
                (IFederationMember[] miners, (List<IFederationMember> members, HashSet<IFederationMember> whoJoined)[] federations) = this.federationHistory.GetFederationMembersForBlocks(headers, false);

                for (int i = 0; i < headers.Length; i++)
                {
                    ChainedHeader header = headers[i];

                    uint headerTime = header.Header.Time;

                    if (miners[i] != null)
                    {
                        if (!this.lastActiveTimes.TryGetValue(miners[i].PubKey, out SortedList<uint, ChainedHeader> minerActivity))
                        {
                            minerActivity = new SortedList<uint, ChainedHeader>();
                            this.lastActiveTimes[miners[i].PubKey] = minerActivity;
                        }

                        minerActivity[headerTime] = header;
                    }

                    foreach (IFederationMember member in federations[i].whoJoined)
                    {
                        if (!this.lastActiveTimes.TryGetValue(member.PubKey, out SortedList<uint, ChainedHeader> joinActivity))
                        {
                            joinActivity = new SortedList<uint, ChainedHeader>();
                            this.lastActiveTimes[member.PubKey] = joinActivity;
                        }

                        joinActivity[headerTime] = header;
                    }
                }
            }

            this.lastActiveTip = blockHeader;
        }

        /// <inheritdoc />
        public bool GetLastActiveTime(IFederationMember federationMember, ChainedHeader blockHeader, out ChainedHeader lastActiveHeader)
        {
            UpdateTip(blockHeader);

            if (this.lastActiveTimes.TryGetValue(federationMember.PubKey, out SortedList<uint, ChainedHeader> activity))
            {
                uint blockTime = blockHeader.Header.Time;

                lastActiveHeader = activity.Values.Last();
                if (activity.Keys.Last() <= blockTime)
                    return true;

                int pos = BinarySearch.BinaryFindFirst(i => activity.Keys[i] > blockTime, 0, activity.Count);

                if (pos > 0)
                {
                    lastActiveHeader = activity.Values[pos - 1];
                    return true;
                }
            }

            if (((PoAConsensusOptions)this.network.Consensus.Options).GenesisFederationMembers.Any(m => m.PubKey == federationMember.PubKey))
            {
                lastActiveHeader = null;
                return true;
            }

            // This should never happen.
            this.logger.LogWarning("Could not resolve federation member's first activity.");

            lastActiveHeader = null;
            return false;
        }

        /// <inheritdoc />
        public bool ShouldMemberBeKicked(IFederationMember federationMember, ChainedHeader blockHeader, ChainedHeader currentTip, out uint inactiveForSeconds)
        {
            Guard.NotNull(federationMember, nameof(federationMember));

            PubKey pubKey = federationMember.PubKey;

            if (this.lastActiveTimes == null)
                throw new Exception($"'{nameof(IdleFederationMembersKicker)}' has not been initialized.");

            uint lastActiveTime = this.network.GenesisTime;
            if (this.GetLastActiveTime(federationMember, currentTip, out ChainedHeader lastActiveHeader))
                lastActiveTime = lastActiveHeader?.Header.Time ?? this.network.GenesisTime;

            uint blockTime = blockHeader.Header.Time;

            inactiveForSeconds = blockTime - lastActiveTime;

            // This might happen in test setup scenarios.
            if (blockTime < lastActiveTime)
                inactiveForSeconds = 0;

            return inactiveForSeconds > this.federationMemberMaxIdleTimeSeconds && !((CollateralFederationMember)federationMember).IsMultisigMember;
        }

        /// <inheritdoc />
        public void Execute(ChainedHeader consensusTip)
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

                List<IFederationMember> modifiedFederation = this.federationHistory.GetFederationForBlock(this.lastActiveTip);

                /*
                PubKey pubKey = this.federationHistory.GetFederationMemberForBlock(consensusTip, modifiedFederation).PubKey;
                this.fedPubKeysByLastActiveTime.AddOrReplace(pubKey, consensusTip.Header.Time);
                this.fedPubKeysByLastActiveTime1.AddOrReplace(pubKey, consensusTip);

                this.SaveMembersByLastActiveTime();
                */

                // Check if any fed member was idle for too long. Use the timestamp of the mined block.
                foreach (KeyValuePair<PubKey, uint> fedMemberToActiveTime in this.GetFederationMembersByLastActiveTime())
                {
                    IFederationMember federationMember = modifiedFederation.FirstOrDefault(m => m.PubKey == fedMemberToActiveTime.Key);
                    if (federationMember == null)
                        continue;

                    if (this.ShouldMemberBeKicked(federationMember, consensusTip, consensusTip, out uint inactiveForSeconds))
                    {
                        IFederationMember memberToKick = this.federationManager.GetFederationMembers().SingleOrDefault(x => x.PubKey == fedMemberToActiveTime.Key);

                        byte[] federationMemberBytes = this.consensusFactory.SerializeFederationMember(memberToKick);

                        bool alreadyKicking = this.votingManager.AlreadyVotingFor(VoteKey.KickFederationMember, federationMemberBytes);

                        if (!alreadyKicking)
                        {
                            this.logger.LogWarning("Federation member '{0}' was inactive for {1} seconds and will be scheduled to be kicked.", fedMemberToActiveTime.Key, inactiveForSeconds);

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

        /// <inheritdoc />
        public void SaveMembersByLastActiveTime()
        {
            var members = this.GetFederationMembersByLastActiveTime();
            var dataToSave = new Dictionary<string, uint>();

            foreach (KeyValuePair<PubKey, uint> pair in members)
                dataToSave.Add(pair.Key.ToHex(), pair.Value);

            this.keyValueRepository.SaveValueJson(fedMembersByLastActiveTimeKey, dataToSave);
        }

        /// <inheritdoc />
        public void Dispose()
        {
        }
    }
}
