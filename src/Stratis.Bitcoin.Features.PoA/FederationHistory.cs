using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Gets the federation member for a specified block by first looking at a cache 
        /// and then the signature in <see cref="PoABlockHeader.BlockSignature"/>.</summary>
        /// <param name="chainedHeader">Identifies the block and timestamp.</param>
        /// <param name="federation">The federation members at the block height.</param>
        /// <returns>The federation member or <c>null</c> if the member could not be determined.</returns>
        IFederationMember GetFederationMemberForBlock(ChainedHeader chainedHeader, List<IFederationMember> federation);

        IEnumerable<IFederationMember> GetFederationMembersForBlocks(IEnumerable<ChainedHeader> chainedHeaders);

        /// <summary>Gets the federation for a specified block.</summary>
        /// <param name="chainedHeader">Identifies the block and timestamp.</param>
        /// <returns>The federation member or <c>null</c> if the member could not be determined.</returns>
        List<IFederationMember> GetFederationForBlock(ChainedHeader chainedHeader);

        /// <summary>Determines if the federation for a specified block can be determined based on the available poll information.</summary>
        /// <param name="chainedHeader">Identifies the block and timestamp.</param>
        /// <returns><c>True</c> if the federation can be determined and <c>false</c> otherwise.</returns>
        bool CanGetFederationForBlock(ChainedHeader chainedHeader);

        /// <summary>
        /// See <see cref="PoAConsensusOptions.VotingManagerV2ActivationHeight"/>
        /// </summary>
        /// <returns>The federation member or <c>null</c> if the member could not be determined.</returns>
        IFederationMember GetFederationMemberForTimestamp(ChainedHeader chainedHeader, PoAConsensusOptions poAConsensusOptions, List<IFederationMember> modifiedFederation = null);
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
        internal readonly Dictionary<uint256, IFederationMember> minersByBlockHash;

        public FederationHistory(IFederationManager federationManager, VotingManager votingManager)
        {
            this.federationManager = federationManager;
            this.votingManager = votingManager;
            this.minersByBlockHash = new Dictionary<uint256, IFederationMember>();
        }

        /// <inheritdoc />
        public bool CanGetFederationForBlock(ChainedHeader chainedHeader)
        {
            return this.votingManager.CanGetFederationForBlock(chainedHeader);
        }

        /// <inheritdoc />
        public List<IFederationMember> GetFederationForBlock(ChainedHeader chainedHeader)
        {
            return (this.votingManager == null) ? this.federationManager.GetFederationMembers() : this.votingManager.GetModifiedFederation(chainedHeader);
        }

        /// <inheritdoc />
        public IFederationMember GetFederationMemberForBlock(ChainedHeader chainedHeader)
        {
            if (this.minersByBlockHash.TryGetValue(chainedHeader.HashBlock, out IFederationMember federationMember))
                return federationMember;

            return GetFederationMemberForBlockInternal(chainedHeader, this.GetFederationForBlock(chainedHeader));
        }

        /// <inheritdoc />
        public IEnumerable<IFederationMember> GetFederationMembersForBlocks(IEnumerable<ChainedHeader> chainedHeaders)
        {
            List<IFederationMember>[] federations = this.votingManager.GetModifiedFederations(chainedHeaders).ToArray();

            return chainedHeaders.Select((chainedHeader, n) => this.minersByBlockHash.TryGetValue(chainedHeader.HashBlock, out IFederationMember federationMember) ? federationMember : 
                GetFederationMemberForBlockInternal(chainedHeader, federations[n]));
        }

        /// <inheritdoc />
        public IFederationMember GetFederationMemberForBlock(ChainedHeader chainedHeader, List<IFederationMember> federation)
        {
            if (this.minersByBlockHash.TryGetValue(chainedHeader.HashBlock, out IFederationMember federationMember))
                return federationMember;

            return GetFederationMemberForBlockInternal(chainedHeader, federation);
        }

        private IFederationMember GetFederationMemberForBlockInternal(ChainedHeader chainedHeader, List<IFederationMember> federation)
        {
            if (chainedHeader.Height == 0)
                return federation.Last();

            // Try to provide the public key that signed the block.
            try
            {
                var header = chainedHeader.Header as PoABlockHeader;

                var signature = ECDSASignature.FromDER(header.BlockSignature.Signature);
                for (int recId = 0; recId < 4; recId++)
                {
                    PubKey pubKeyForSig = PubKey.RecoverFromSignature(recId, signature, header.GetHash(), true);
                    if (pubKeyForSig == null)
                        break;

                    IFederationMember federationMember = federation.FirstOrDefault(m => m.PubKey == pubKeyForSig);
                    if (federationMember != null)
                    {
                        this.minersByBlockHash[chainedHeader.HashBlock] = federationMember;
                        return federationMember;
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <inheritdoc />
        public IFederationMember GetFederationMemberForTimestamp(ChainedHeader chainedHeader, PoAConsensusOptions poAConsensusOptions, List<IFederationMember> modifiedFederation = null)
        {
            List<IFederationMember> federationMembers = modifiedFederation ?? this.federationManager.GetFederationMembers(chainedHeader);

            uint roundTime = this.GetRoundLengthSeconds(poAConsensusOptions, federationMembers.Count);

            uint headerUnixTimestamp = chainedHeader.Header.Time;

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
