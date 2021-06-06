using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Crypto;
using Stratis.Bitcoin.Features.PoA.Voting;

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

        /// <summary>Gets the miner for a specified block by first looking at a cache 
        /// and then the signature in <see cref="PoABlockHeader.BlockSignature"/>.</summary>
        /// <param name="chainedHeader">Identifies the block and timestamp.</param>
        /// <param name="federation">The federation members at the block height.</param>
        /// <returns>The federation member or <c>null</c> if the member could not be determined.</returns>
        IFederationMember GetFederationMemberForBlock(ChainedHeader chainedHeader, List<IFederationMember> federation);

        IFederationMember GetFederationMemberForBlock(PoABlockHeader blockHeader, List<IFederationMember> federation, bool votingManagerV2);

        /// <summary>Gets the miner (and federation) for the specified blocks by first looking at a cache 
        /// and then the signature in <see cref="PoABlockHeader.BlockSignature"/>.</summary>
        /// <param name="chainedHeaders">Identifies the blocks and timestamps.</param>
        /// <returns>The federation member or <c>null</c> if the member could not be determined, as well as the federations.</returns>
        (IFederationMember[] miners, (List<IFederationMember> members, HashSet<IFederationMember> whoJoined)[] federations) GetFederationMembersForBlocks(ChainedHeader[] chainedHeaders, bool forceV2 = false);

        /// <summary>Gets the federation for a specified block.</summary>
        /// <param name="chainedHeader">Identifies the block and timestamp.</param>
        /// <returns>The federation member or <c>null</c> if the member could not be determined.</returns>
        List<IFederationMember> GetFederationForBlock(ChainedHeader chainedHeader);

        /// <summary>
        /// See <see cref="PoAConsensusOptions.VotingManagerV2ActivationHeight"/>
        /// </summary>
        /// <returns>The federation member or <c>null</c> if the member could not be determined.</returns>
        IFederationMember GetFederationMemberForTimestamp(uint headerUnixTimestamp, PoAConsensusOptions poAConsensusOptions, List<IFederationMember> federationMembers = null);

        /// <summary>
        /// Determines the height from which the voting manager v2 is active.
        /// </summary>
        /// <returns>The height from which the voting manager v2 is active.</returns>
        int GetVotingManagerV2ActivationHeight();
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
        private readonly Network network;
        private readonly ConcurrentDictionary<uint256, PubKey[]> candidatesByBlockHash;

        public FederationHistory(IFederationManager federationManager, Network network, VotingManager votingManager = null)
        {
            this.federationManager = federationManager;
            this.votingManager = votingManager;
            this.network = network;
            this.candidatesByBlockHash = new ConcurrentDictionary<uint256, PubKey[]>();
        }

        /// <inheritdoc />
        public List<IFederationMember> GetFederationForBlock(ChainedHeader chainedHeader)
        {
            return (this.votingManager == null) ? this.federationManager.GetFederationMembers() : this.votingManager.GetModifiedFederation(chainedHeader);
        }

        /// <inheritdoc />
        public IFederationMember GetFederationMemberForBlock(ChainedHeader chainedHeader)
        {
            return GetFederationMemberForBlock(chainedHeader, this.GetFederationForBlock(chainedHeader));
        }

        public int GetVotingManagerV2ActivationHeight()
        {
            if (this.network.Consensus.Options is PoAConsensusOptions poaConsensusOptions)
                return (poaConsensusOptions.VotingManagerV2ActivationHeight == 0) ? int.MaxValue : poaConsensusOptions.VotingManagerV2ActivationHeight;

            return 0;
        }

        /// <inheritdoc />
        public IFederationMember GetFederationMemberForBlock(ChainedHeader chainedHeader, List<IFederationMember> federation)
        {
            if (chainedHeader.Height == 0)
                return federation.Last();

            return GetFederationMemberForBlock(chainedHeader.Header as PoABlockHeader, federation, chainedHeader.Height >= this.GetVotingManagerV2ActivationHeight());
        }

        /// <inheritdoc />
        public (IFederationMember[] miners, (List<IFederationMember> members, HashSet<IFederationMember> whoJoined)[] federations) GetFederationMembersForBlocks(ChainedHeader[] chainedHeaders, bool forceV2 = false)
        {
            (List<IFederationMember> members, HashSet<IFederationMember> whoJoined)[] federations = this.votingManager.GetModifiedFederations(chainedHeaders).ToArray();
            IFederationMember[] miners = new IFederationMember[chainedHeaders.Length];
            PoABlockHeader[] headers = chainedHeaders.Select(h => (PoABlockHeader)h.Header).ToArray();

            // Reading chainedHeader's "Header" does not play well with asynchronocity so we will load the block times here.
            int votingManagerV2ActivationHeight = GetVotingManagerV2ActivationHeight();

            Parallel.For(0, chainedHeaders.Length, i => miners[i] = GetFederationMemberForBlock(headers[i], federations[i].members, forceV2 || chainedHeaders[i].Height >= votingManagerV2ActivationHeight));

            if (chainedHeaders.FirstOrDefault()?.Height == 0)
                miners[0] = federations[0].members.Last();

            return (miners, federations);
        }

        public IFederationMember GetFederationMemberForBlock(PoABlockHeader blockHeader, List<IFederationMember> federation, bool votingManagerV2)
        {
            if (!votingManagerV2)
                return GetFederationMemberForTimestamp(blockHeader.Time, this.network.Consensus.Options as PoAConsensusOptions, federation);

            uint256 blockHash = blockHeader.GetHash();

            if (!this.candidatesByBlockHash.TryGetValue(blockHash, out PubKey[] pubKeys))
            {
                pubKeys = new PubKey[4];

                try
                {
                    var signature = ECDSASignature.FromDER(blockHeader.BlockSignature.Signature);
                    for (int recId = 0; recId < pubKeys.Length; recId++)
                        pubKeys[recId] = PubKey.RecoverFromSignature(recId, signature, blockHash, true);
                }
                catch (Exception)
                {
                }

                this.candidatesByBlockHash[blockHash] = pubKeys;
            }

            foreach (PubKey pubKeyForSig in pubKeys)
            { 
                IFederationMember federationMember = federation.FirstOrDefault(m => m.PubKey == pubKeyForSig);
                if (federationMember != null)
                    return federationMember;
            }

            return null;
        }

        /// <inheritdoc />
        public IFederationMember GetFederationMemberForTimestamp(uint headerUnixTimestamp, PoAConsensusOptions poAConsensusOptions, List<IFederationMember> federationMembers = null)
        {
            federationMembers = federationMembers ?? this.federationManager.GetFederationMembers();

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
    }
}
