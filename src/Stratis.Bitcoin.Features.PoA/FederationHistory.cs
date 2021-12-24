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
        /// <summary>Determines if the federation for a specified block can be determined based on the available poll information.</summary>
        /// <param name="chainedHeader">Identifies the block and timestamp.</param>
        /// <returns><c>True</c> if the federation can be determined and <c>false</c> otherwise.</returns>
        bool CanGetFederationForBlock(ChainedHeader chainedHeader);

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

        private SortedDictionary<int, (List<IFederationMember> members, HashSet<IFederationMember> joined, IFederationMember miner)> federationHistory;
        private ConcurrentDictionary<PubKey, List<uint>> lastActiveTimeByPubKey;
        private ChainedHeader lastActiveTip;
        private int lastFederationTip;

        public FederationHistory(IFederationManager federationManager, Network network, VotingManager votingManager = null, ChainIndexer chainIndexer = null, NodeSettings nodeSettings = null)
        {
            this.federationManager = federationManager;
            this.votingManager = votingManager;
            this.chainIndexer = chainIndexer;
            this.network = network;
            this.nodeSettings = nodeSettings;
            this.lockObject = new object();
            this.lastActiveTimeByPubKey = new ConcurrentDictionary<PubKey, List<uint>>();
            this.federationHistory = new SortedDictionary<int, (List<IFederationMember>, HashSet<IFederationMember>, IFederationMember)>();
            this.lastActiveTip = null;
            this.lastFederationTip = -1;
        }

        public void Initialize()
        {
            GetFederationForBlock(this.chainIndexer.Tip);
        }

        /// <inheritdoc />
        public bool CanGetFederationForBlock(ChainedHeader chainedHeader)
        {
            return this.votingManager.CanGetFederationForBlock(chainedHeader);
        }

        /// <inheritdoc />
        public List<IFederationMember> GetFederationForBlock(ChainedHeader chainedHeader)
        {
            lock (this.lockObject)
            {
                if ((this.lastActiveTip == chainedHeader || this.lastActiveTip?.FindFork(chainedHeader)?.Height >= chainedHeader.Height) &&
                    this.federationHistory.TryGetValue(chainedHeader.Height, out (List<IFederationMember> modifiedFederation, HashSet<IFederationMember> whoJoined, IFederationMember miner) item))
                {
                    return item.modifiedFederation;
                }

                this.UpdateTip(chainedHeader);

                if (this.federationHistory.TryGetValue(chainedHeader.Height, out item))
                    return item.modifiedFederation;

                return this.GetFederationsForHeightsNoCache(chainedHeader.Height, chainedHeader.Height).First().members;
            }
        }

        private IEnumerable<(List<IFederationMember> members, HashSet<IFederationMember> whoJoined)> GetFederationsForHeightsNoCache(int startHeight, int endHeight)
        {
            IEnumerable<(List<IFederationMember> members, HashSet<IFederationMember> whoJoined)> federations;

            if (this.votingManager != null && this.nodeSettings.DevMode == null)
                federations = this.votingManager.GetFederationsForHeights(startHeight, endHeight).ToArray();
            else
                federations = Enumerable.Range(0, endHeight - startHeight + 1).Select(n => (this.federationManager.GetFederationMembers(), new HashSet<IFederationMember>())).ToArray();

            return federations;
        }

        /// <inheritdoc />
        public IFederationMember GetFederationMemberForBlock(ChainedHeader chainedHeader)
        {
            lock (this.lockObject)
            {
                if ((this.lastActiveTip == chainedHeader || this.lastActiveTip?.FindFork(chainedHeader)?.Height >= chainedHeader.Height) &&
                    this.federationHistory.TryGetValue(chainedHeader.Height, out (List<IFederationMember> modifiedFederation, HashSet<IFederationMember>, IFederationMember miner) item) &&
                    item.miner != null)
                {
                    return item.miner;
                }

                this.UpdateTip(chainedHeader);

                if (this.federationHistory.TryGetValue(chainedHeader.Height, out item) && item.miner != null)
                    return item.miner;

                return this.GetFederationMembersForBlocks(chainedHeader, 1)[0];
            }
        }

        private IFederationMember[] GetFederationMembersForBlocks(ChainedHeader lastHeader, int count)
        {
            // Reading chainedHeader's "Header" does not play well with asynchronocity so we will load it here.
            PoABlockHeader[] headers = lastHeader.EnumerateToGenesis().Take(count).Reverse().Select(h => (PoABlockHeader)h.Header).ToArray();

            IFederationMember[] miners = new IFederationMember[headers.Length];

            // Reading chainedHeader's "Header" does not play well with asynchronocity so we will load the block times here.
            int votingManagerV2ActivationHeight = (this.network.Consensus.Options as PoAConsensusOptions).VotingManagerV2ActivationHeight;

            int startHeight = lastHeader.Height + 1 - count;

            Parallel.For(0, headers.Length, i => miners[i] = GetFederationMemberForBlock(headers[i], this.federationHistory[i + startHeight].members, (i + startHeight) >= votingManagerV2ActivationHeight));

            if (startHeight == 0)
                miners[0] = this.federationHistory[0].members.Last();

            return miners;
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

            int firstHeight = this.federationHistory.ElementAt(0).Key;
            int count = Math.Min(this.federationHistory.Count, this.chainIndexer.Tip.Height + 1 - firstHeight);
            int pos2 = BinarySearch.BinaryFindFirst(x => this.chainIndexer.GetHeader(x).Header.Time > discardAboveTime, firstHeight, count) - firstHeight;
            if (pos2 > 0)
                this.federationHistory = new SortedDictionary<int, (List<IFederationMember>, HashSet<IFederationMember>, IFederationMember)>(this.federationHistory.Skip(pos2).ToDictionary(x => x.Key, x => x.Value));
        }

        private void DiscardActivityBelowTime(uint discardBelowTime)
        {
            const int discardThreshold = 1000;

            // If there is more than the threshold amount of extraneous history then discard it.
            int firstHeight = this.federationHistory.ElementAt(0).Key;
            int count = Math.Min(this.federationHistory.Count, this.chainIndexer.Tip.Height + 1 - firstHeight);
            int pos2 = BinarySearch.BinaryFindFirst(x => this.chainIndexer.GetHeader(x).Header.Time > discardBelowTime, firstHeight, count) - firstHeight;
            if (pos2 < discardThreshold)
                return;

            this.federationHistory = new SortedDictionary<int, (List<IFederationMember>, HashSet<IFederationMember>, IFederationMember)>(this.federationHistory.Skip(pos2).ToDictionary(x => x.Key, x => x.Value));

            discardBelowTime = this.chainIndexer.GetHeader(this.federationHistory.ElementAt(0).Key).Header.Time;

            var remove = new List<PubKey>();

            foreach ((PubKey pubKey, List<uint> activity) in this.lastActiveTimeByPubKey)
            {
                int pos = BinarySearch.BinaryFindFirst(x => activity[x] >= discardBelowTime, 0, activity.Count);
                if (pos >= 0)
                {
                    if (activity.Count <= pos)
                        remove.Add(pubKey);
                    else
                        activity.RemoveRange(0, pos);
                }
            }

            foreach (PubKey pubKey in remove)
                this.lastActiveTimeByPubKey.Remove(pubKey, out _);
        }


        /// <summary>
        /// Updates this object with recent federation history up to the passed <paramref name="blockHeader"/>.
        /// Recent federation history includes all blocks within the last <see cref="PoAConsensusOptions.FederationMemberActivationTime"/> seconds.
        /// </summary>
        /// <param name="blockHeader">The block up to which we require recent history.</param>
        private void UpdateTip(ChainedHeader blockHeader)
        {
            // If there is already information recorded.
            if (this.lastActiveTip != null)
            {
                // If the information recorded is current then exit.
                if (blockHeader == this.lastActiveTip)
                    return;

                // Find the fork point between the recorded information and the block of interest.
                ChainedHeader fork = this.lastActiveTip.FindFork(blockHeader);

                // If the recorded history includes the block then do nothing.
                if (fork == blockHeader)
                    return;

                // If the fork shows blocks that are not in common then discard those blocks.
                if (fork != this.lastActiveTip)
                {
                    DiscardActivityAboveTime(fork.Header.Time);
                    this.lastActiveTip = fork;
                    this.lastFederationTip = fork.Height;
                }
            }

            // Gather more blocks than required if we're at the consensus tip and additional federations can be determined.
            int endHeight = blockHeader.Height;
            if (blockHeader.Height >= this.chainIndexer.Tip.Height && this.votingManager.LastKnownFederationHeight() > blockHeader.Height)
                endHeight = this.votingManager.LastKnownFederationHeight();

            // Gather enough blocks to handle idle checking but nothing below the activation time.
            uint federationMemberActivationTime = ((PoAConsensusOptions)this.network.Consensus.Options).FederationMemberActivationTime ?? 0;
            uint minTime = blockHeader.Header.Time;
            if (minTime >= federationMemberActivationTime)
            {
                minTime -= ((PoAConsensusOptions)this.network.Consensus.Options).FederationMemberMaxIdleTimeSeconds;
                if (minTime < federationMemberActivationTime)
                    minTime = federationMemberActivationTime;
            }

            // This method works even if the block header height relate to blocks being connected above the consensus tip.
            ChainedHeader GetHeader(int height)
            {
                if (height < this.chainIndexer.Tip.Height)
                    return this.chainIndexer[height];

                return blockHeader.GetAncestor(height);
            }
            
            // Find the first block with Time >= minTime. We're not interested in re-reading any blocks below or at the last active tip though.
            int startHeight = (this.lastActiveTip?.Height ?? -1) + 1;
            startHeight = BinarySearch.BinaryFindFirst(n => GetHeader(n).Header.Time >= minTime, startHeight, blockHeader.Height - startHeight + 1);

            // Exclude anything in cache already.
            int fedStartHeight = Math.Max(startHeight, this.lastFederationTip + 1);

            // If we need to determine more federation make-ups to catch up with the blockHeader height...
            if (fedStartHeight <= blockHeader.Height)
            {
                // Determine as many federations as we can, possibly pre-fetching beyond the blockHeader.
                IEnumerable<(List<IFederationMember> federation, HashSet<IFederationMember> whoJoined)> federations = GetFederationsForHeightsNoCache(fedStartHeight, endHeight);

                // Record the info.
                foreach ((List<IFederationMember> federation, HashSet<IFederationMember> whoJoined) in federations)
                    this.federationHistory[fedStartHeight++] = (federation, whoJoined, null); // Miner not known yet.

                this.lastFederationTip = endHeight;
            }

            // Determine the miners.
            IFederationMember[] miners = this.GetFederationMembersForBlocks(blockHeader, blockHeader.Height - startHeight + 1);

            foreach (ChainedHeader header in blockHeader.EnumerateToGenesis().Take(miners.Length).Reverse())
            {
                // Add the miner to the history.
                (List<IFederationMember> members, HashSet<IFederationMember> joined, IFederationMember miner) history = this.federationHistory[header.Height];
                history.miner = miners[header.Height - startHeight];
                this.federationHistory[header.Height] = history;

                // Don't record any activity before the federation activation time.
                uint headerTime = header.Header.Time;
                if (headerTime < federationMemberActivationTime)
                    continue;

                // Record mining activity.
                if (history.miner != null)
                {
                    if (!this.lastActiveTimeByPubKey.TryGetValue(history.miner.PubKey, out List<uint> minerActivity))
                    {
                        minerActivity = new List<uint>();
                        this.lastActiveTimeByPubKey[history.miner.PubKey] = minerActivity;
                    }

                    if (minerActivity.LastOrDefault() != headerTime)
                        minerActivity.Add(headerTime);
                }

                // Record joining activity.
                foreach (IFederationMember member in history.joined)
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

            // Advance the tip.
            this.lastActiveTip = blockHeader;

            DiscardActivityBelowTime(minTime);
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
