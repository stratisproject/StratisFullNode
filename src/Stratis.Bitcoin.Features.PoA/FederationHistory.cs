using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Crypto;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA
{
    public interface IFederationHistory
    {
        /// <summary>Gets the federation member for a specified block by first looking at a cache 
        /// and then the signature in <see cref="PoABlockHeader.BlockSignature"/>.</summary>
        /// <param name="chainedHeader">Identifies the block and timestamp.</param>
        /// <returns>The federation member or <c>null</c> if the member could not be determined.</returns>
        /// <exception cref="ConsensusErrorException">In case timestamp is invalid.</exception>
        IFederationMember GetFederationMemberForBlock(ChainedHeader chainedHeader);

        /// <summary>Gets the federation for a specified block.</summary>
        /// <param name="chainedHeader">Identifies the block and timestamp.</param>
        /// <returns>The federation member or <c>null</c> if the member could not be determined.</returns>
        List<IFederationMember> GetFederationForBlock(ChainedHeader chainedHeader);

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
        /// Initializes this component.
        /// </summary>
        void Initialize();
    }

    /// <summary>
    /// This component can be used to determine the member that mined a block for any block height.
    /// It also provides the federation at any block height by leveraging the functionality
    /// implemented by <see cref="VotingManager.GetModifiedFederation(ChainedHeader)"/>.
    /// </summary>
    public class FederationHistory : IFederationHistory
    {
        private readonly IFederationManager federationManager;
        private readonly VotingManager votingManager;
        private readonly ChainIndexer chainIndexer;
        private readonly Network network;
        private readonly NodeSettings nodeSettings;
        private readonly object lockObject;

        private SortedDictionary<uint, (List<IFederationMember>, IFederationMember)> federationHistory;
        private ConcurrentDictionary<PubKey, List<uint>> lastActiveTimeByPubKey;
        private ChainedHeader lastActiveTip;

        public FederationHistory(IFederationManager federationManager, Network network, VotingManager votingManager = null, ChainIndexer chainIndexer = null, NodeSettings nodeSettings = null)
        {
            this.federationManager = federationManager;
            this.votingManager = votingManager;
            this.chainIndexer = chainIndexer;
            this.network = network;
            this.nodeSettings = nodeSettings;
            this.lockObject = new object();
            this.lastActiveTimeByPubKey = new ConcurrentDictionary<PubKey, List<uint>>();
            this.federationHistory = new SortedDictionary<uint, (List<IFederationMember>, IFederationMember)>();
            this.lastActiveTip = null;
        }

        public void Initialize()
        {
            GetFederationForBlock(this.chainIndexer.Tip);
        }

        /// <inheritdoc />
        public List<IFederationMember> GetFederationForBlock(ChainedHeader chainedHeader)
        {
            lock (this.lockObject)
            {
                this.UpdateTip(chainedHeader);

                if (this.federationHistory.TryGetValue(chainedHeader.Header.Time, out (List<IFederationMember> modifiedFederation, IFederationMember miner) item))
                    return item.modifiedFederation;

                return this.GetFederationMembersForBlocks(new[] { chainedHeader }).federations[0].members;
            }
        }

        /// <inheritdoc />
        public IFederationMember GetFederationMemberForBlock(ChainedHeader chainedHeader)
        {
            lock (this.lockObject)
            {
                this.UpdateTip(chainedHeader);

                if (this.federationHistory.TryGetValue(chainedHeader.Header.Time, out (List<IFederationMember> modifiedFederation, IFederationMember miner) item))
                    return item.miner;

                return this.GetFederationMembersForBlocks(new[] { chainedHeader }).miners[0];
            }
        }

        private (IFederationMember[] miners, (List<IFederationMember> members, HashSet<IFederationMember> whoJoined)[] federations) GetFederationMembersForBlocks(ChainedHeader[] chainedHeaders)
        {
            (List<IFederationMember> members, HashSet<IFederationMember> whoJoined)[] federations;
            if (this.votingManager != null && this.nodeSettings.DevMode == null)
                federations = this.votingManager.GetModifiedFederations(chainedHeaders).ToArray();
            else
                federations = Enumerable.Range(0, chainedHeaders.Length).Select(n => (this.federationManager.GetFederationMembers(), new HashSet<IFederationMember>())).ToArray();

            IFederationMember[] miners = new IFederationMember[chainedHeaders.Length];

            // Reading chainedHeader's "Header" does not play well with asynchronocity so we will load it here.
            PoABlockHeader[] headers = chainedHeaders.Select(h => (PoABlockHeader)h.Header).ToArray();

            // Reading chainedHeader's "Header" does not play well with asynchronocity so we will load the block times here.
            int votingManagerV2ActivationHeight = (this.network.Consensus.Options as PoAConsensusOptions).VotingManagerV2ActivationHeight;

            Parallel.For(0, chainedHeaders.Length, i => miners[i] = GetFederationMemberForBlock(headers[i], federations[i].members, chainedHeaders[i].Height >= votingManagerV2ActivationHeight));

            if (chainedHeaders.FirstOrDefault()?.Height == 0)
                miners[0] = federations[0].members.Last();

            return (miners, federations);
        }

        private IFederationMember GetFederationMemberForBlock(PoABlockHeader blockHeader, List<IFederationMember> federation, bool votingManagerV2)
        {
            if (!votingManagerV2)
                return GetFederationMemberForTimestamp(blockHeader.Time, federation);

            uint256 blockHash = blockHeader.GetHash();

            try
            {
                var signature = ECDSASignature.FromDER(blockHeader.BlockSignature.Signature);
                for (int recId = 0; recId < 4; recId++)
                {
                    PubKey pubKeyForSig = PubKey.RecoverFromSignature(recId, signature, blockHash, true);
                    if (pubKeyForSig == null)
                        break;

                    IFederationMember federationMember = federation.FirstOrDefault(m => m.PubKey == pubKeyForSig);
                    if (federationMember != null)
                        return federationMember;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private IFederationMember GetFederationMemberForTimestamp(uint headerUnixTimestamp, List<IFederationMember> federationMembers)
        {
            federationMembers = federationMembers ?? this.federationManager.GetFederationMembers();

            var poAConsensusOptions = (PoAConsensusOptions)this.network.Consensus.Options;

            uint roundTime = this.GetRoundLengthSeconds(poAConsensusOptions, federationMembers.Count);

            // Time when current round started.
            uint roundStartTimestamp = (headerUnixTimestamp / roundTime) * roundTime;

            // Slot number in current round.
            int currentSlotNumber = (int)((headerUnixTimestamp - roundStartTimestamp) / poAConsensusOptions.TargetSpacingSeconds);

            return federationMembers[currentSlotNumber];
        }

        private uint GetRoundLengthSeconds(PoAConsensusOptions poAConsensusOptions, int? federationMembersCount)
        {
            uint roundLength = (uint)(federationMembersCount * poAConsensusOptions.TargetSpacingSeconds);
            return roundLength;
        }

        /// <inheritdoc />
        public ConcurrentDictionary<IFederationMember, uint> GetFederationMembersByLastActiveTime()
        {
            lock (this.lockObject)
            {
                ChainedHeader tip = this.lastActiveTip ?? this.chainIndexer.GetHeader(0);

                List<IFederationMember> federationMembers = this.GetFederationForBlock(tip);

                return new ConcurrentDictionary<IFederationMember, uint>(federationMembers
                    .Select(m => (m, (this.GetLastActiveTime(m, tip, out uint lastActiveTime) && lastActiveTime != default) ? lastActiveTime : this.network.GenesisTime))
                    .ToDictionary(x => x.m, x => x.Item2));
            }
        }

        private void DiscardActivityAboveTime(uint discardAboveTime)
        {
            var remove = new List<PubKey>();

            foreach ((PubKey pubKey, List<uint> activity) in this.lastActiveTimeByPubKey)
            {
                int pos = BinarySearch.BinaryFindFirst(x => activity[x] > discardAboveTime, 0, activity.Count);
                if (pos >= 0)
                {
                    if (pos == 0)
                        remove.Add(pubKey);
                    else
                        activity.RemoveRange(pos, activity.Count - pos);
                }
            }

            foreach (PubKey pubKey in remove)
                this.lastActiveTimeByPubKey.Remove(pubKey, out _);

            int pos2 = BinarySearch.BinaryFindFirst(x => this.federationHistory.ElementAt(x).Key > discardAboveTime, 0, this.federationHistory.Values.Count);
            if (pos2 > 0)
                this.federationHistory = new SortedDictionary<uint, (List<IFederationMember>, IFederationMember)>(this.federationHistory.Skip(pos2).ToDictionary(x => x.Key, x => x.Value));
        }

        private void DiscardActivityBelowTime(uint discardBelowTime)
        {
            // Discard 500 blocks if there is more than 1000 extraneous blocks.
            int pos2 = BinarySearch.BinaryFindFirst(x => this.federationHistory.ElementAt(x).Key > discardBelowTime, 0, this.federationHistory.Values.Count);
            if (pos2 < 1000)
                return;

            this.federationHistory = new SortedDictionary<uint, (List<IFederationMember>, IFederationMember)>(this.federationHistory.Skip(500).ToDictionary(x => x.Key, x => x.Value));

            discardBelowTime = this.federationHistory.ElementAt(0).Key;

            var remove = new List<PubKey>();

            foreach ((PubKey pubKey, List<uint> activity) in this.lastActiveTimeByPubKey)
            {
                int pos = BinarySearch.BinaryFindFirst(x => activity[x] >= discardBelowTime, 0, activity.Count);
                if (pos >= 0)
                {
                    if (activity.Count <= pos )
                        remove.Add(pubKey);
                    else
                        activity.RemoveRange(0, pos);
                }
            }

            foreach (PubKey pubKey in remove)
                this.lastActiveTimeByPubKey.Remove(pubKey, out _);
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

            // Gather enough blocks to handle idle checking but nothing below the activation time.
            uint federationMemberActivationTime = ((PoAConsensusOptions)this.network.Consensus.Options).FederationMemberActivationTime ?? 0;
            uint maxInactiveTime = ((PoAConsensusOptions)this.network.Consensus.Options).FederationMemberMaxIdleTimeSeconds;
            uint blockTime = blockHeader.Header.Time;
            uint maxTime = Math.Max(blockTime - maxInactiveTime, federationMemberActivationTime);

            ChainedHeader[] headers = blockHeader
                .EnumerateToGenesis()
                .TakeWhile(h => h.HashBlock != this.lastActiveTip?.HashBlock && h.Header.Time >= maxTime)
                .Reverse().ToArray();

            if (headers.Length != 0)
            {
                (IFederationMember[] miners, (List<IFederationMember> members, HashSet<IFederationMember> whoJoined)[] federations) = this.GetFederationMembersForBlocks(headers);

                for (int i = 0; i < headers.Length; i++)
                {
                    ChainedHeader header = headers[i];

                    uint headerTime = header.Header.Time;

                    this.federationHistory[headerTime] = (federations[i].members, miners[i]);

                    if (miners[i] != null)
                    {
                        if (!this.lastActiveTimeByPubKey.TryGetValue(miners[i].PubKey, out List<uint> minerActivity))
                        {
                            minerActivity = new List<uint>();
                            this.lastActiveTimeByPubKey[miners[i].PubKey] = minerActivity;
                        }

                        if (minerActivity.LastOrDefault() != headerTime)
                            minerActivity.Add(headerTime);
                    }

                    foreach (IFederationMember member in federations[i].whoJoined)
                    {
                        if (!this.lastActiveTimeByPubKey.TryGetValue(member.PubKey, out List<uint> joinActivity))
                        {
                            joinActivity = new List<uint>();
                            this.lastActiveTimeByPubKey[member.PubKey] = joinActivity;
                        }

                        if (joinActivity.LastOrDefault() != headerTime)
                            joinActivity.Add(headerTime);
                    }
                }
            }

            this.lastActiveTip = blockHeader;

            DiscardActivityBelowTime(maxTime);
        }

        /// <inheritdoc />
        public bool GetLastActiveTime(IFederationMember federationMember, ChainedHeader blockHeader, out uint lastActiveTime)
        {
            lock (this.lockObject)
            {
                UpdateTip(blockHeader);

                if (this.lastActiveTimeByPubKey.TryGetValue(federationMember.PubKey, out List<uint> activity))
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
                lastActiveTime = default;
                return false;
            }
        }
    }
}
