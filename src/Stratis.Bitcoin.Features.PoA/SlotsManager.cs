using System;
using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA
{
    /// <summary>
    /// Provider of information about which pubkey should be used at which timestamp
    /// and what is the next timestamp at which current node will be able to mine.
    /// </summary>
    public interface ISlotsManager
    {

        /// <summary>Gets next timestamp at which current node can produce a block.</summary>
        /// <exception cref="Exception">Thrown if this node is not a federation member.</exception>
        /// <param name="currentTime">The current unix timestamp.</param>
        /// <returns>The next timestamp at which current node can produce a block.</returns>
        uint GetMiningTimestamp(uint currentTime);

        /// <summary>Gets next timestamp at which the miner can produce a block.</summary>
        /// <param name="tip">The previous/last block produced.</param>
        /// <param name="currentTime">The current unix timestamp.</param>
        /// <param name="currentMiner">The miner to find the timestamp for.</param>
        /// <returns>The next timestamp at which the miner can produce a block.</returns>
        uint GetMiningTimestamp(ChainedHeader tip, uint currentTime, PubKey currentMiner);

        /// <summary>Determines whether timestamp is valid according to the network rules.</summary>
        /// <param name="headerUnixTimestamp">The unix timstamp of a block header.</param>
        /// <returns><c>True</c> if the timestamp is valid and <c>false</c> otherwise.</returns>
        bool IsValidTimestamp(uint headerUnixTimestamp);

        TimeSpan GetRoundLength(int? federationMembersCount = null);
    }

    public class SlotsManager : ISlotsManager
    {
        private readonly PoAConsensusOptions consensusOptions;

        private readonly IFederationManager federationManager;

        private readonly IFederationHistory federationHistory;

        private readonly ChainIndexer chainIndexer;

        public SlotsManager(Network network, IFederationManager federationManager, IFederationHistory federationHistory, ChainIndexer chainIndexer)
        {
            Guard.NotNull(network, nameof(network));

            this.federationManager = Guard.NotNull(federationManager, nameof(federationManager));
            this.federationHistory = federationHistory;
            this.chainIndexer = chainIndexer;
            this.consensusOptions = (network as PoANetwork).ConsensusOptions;
        }

        /// <inheritdoc/>
        public uint GetMiningTimestamp(uint currentTime)
        {
            if (!this.federationManager.IsFederationMember)
                throw new NotAFederationMemberException();

            return GetMiningTimestamp(this.chainIndexer.Tip, currentTime, this.federationManager.CurrentFederationKey?.PubKey);
        }

        /// <inheritdoc/>
        public uint GetMiningTimestamp(ChainedHeader tip, uint currentTime, PubKey currentMiner)
        {
            /*
            A miner can calculate when its expected to mine by looking at the ordered list of federation members
            and the last block that was mined and by whom. It can count the number of mining slots from that member
            to itself and multiply that with the target spacing to arrive at its mining timestamp.
            The fact that the federation can change at any time adds extra complexity to this basic approach. 
            The miner that mined the last block may no longer exist when the next block is about to be mined. As such
            we may need to look a bit further back to find a "reference miner" that still occurs in the latest federation.
            */
            if (tip.Height < this.consensusOptions.GetMiningTimestampV2ActivationHeight)
                return GetMiningTimestampLegacy(tip, currentTime, currentMiner);

            List<IFederationMember> federationMembers = this.federationHistory.GetFederationForBlock(tip, 1);
            if (federationMembers == null)
                throw new Exception($"Could not determine the federation at block { tip.Height } + 1.");

            int myIndex = federationMembers.FindIndex(m => m.PubKey == currentMiner);
            if (myIndex < 0)
                throw new NotAFederationMemberException();

            // Find a "reference miner" to determine our slot against.
            ChainedHeader referenceMinerBlock = tip;
            IFederationMember referenceMiner = null;
            int referenceMinerIndex = -1;
            int referenceMinerDepth = 0;
            for (int i = 0; i < federationMembers.Count; i++, referenceMinerDepth++)
            {
                referenceMiner = this.federationHistory.GetFederationMemberForBlock(referenceMinerBlock);
                referenceMinerIndex = federationMembers.FindIndex(m => m.PubKey == referenceMiner.PubKey);
                if (referenceMinerIndex >= 0)
                    break;
            }

            if (referenceMinerIndex < 0)
                throw new Exception("Could not find a member in common between the old and new federation");

            // Found a reference miner that also occurs in the latest federation.
            // Determine how many blocks before our mining slot.
            int blocksFromTipToMiningSlot = myIndex - referenceMinerIndex - referenceMinerDepth;
            while (blocksFromTipToMiningSlot < 0)
                blocksFromTipToMiningSlot += federationMembers.Count;

            // Round length in seconds.
            uint roundTime = (uint)this.GetRoundLength(federationMembers.Count).TotalSeconds;

            // Get the tip time and make is a valid time if required.
            uint tipTime = tip.Header.Time;
            if (!IsValidTimestamp(tipTime))
                tipTime += (this.consensusOptions.TargetSpacingSeconds - tipTime % this.consensusOptions.TargetSpacingSeconds);

            // Check if we have missed our turn for this round.
            // We still consider ourselves "in a turn" if we are in the first half of the turn and we haven't mined there yet.
            // This might happen when starting the node for the first time or if there was a problem when mining.

            uint nextTimestampForMining = (uint)(tipTime + blocksFromTipToMiningSlot * this.consensusOptions.TargetSpacingSeconds);
            while (currentTime > nextTimestampForMining + (this.consensusOptions.TargetSpacingSeconds / 2) // We are closer to the next turn than our own
                  || tipTime == nextTimestampForMining)
                nextTimestampForMining += roundTime;

            return nextTimestampForMining;
        }

        private uint GetMiningTimestampLegacy(ChainedHeader tip, uint currentTime, PubKey currentMiner)
        {
            List<IFederationMember> federationMembers = this.federationHistory.GetFederationForBlock(tip, 1);

            // Round length in seconds.
            uint roundTime = (uint)this.GetRoundLength(federationMembers.Count).TotalSeconds;

            // Index of a slot that current node can take in each round.
            uint slotIndex = (uint)federationMembers.FindIndex(x => x.PubKey == currentMiner);

            // Time when current round started.
            uint roundStartTimestamp = (currentTime / roundTime) * roundTime;
            uint nextTimestampForMining = roundStartTimestamp + slotIndex * this.consensusOptions.TargetSpacingSeconds;

            // Check if we have missed our turn for this round.
            // We still consider ourselves "in a turn" if we are in the first half of the turn and we haven't mined there yet.
            // This might happen when starting the node for the first time or if there was a problem when mining.
            if (currentTime > nextTimestampForMining + (this.consensusOptions.TargetSpacingSeconds / 2) // We are closer to the next turn than our own
                  || tip.Header.Time == nextTimestampForMining) // We have already mined in that slot
            {
                // Get timestamp for next round.
                nextTimestampForMining = roundStartTimestamp + roundTime + slotIndex * this.consensusOptions.TargetSpacingSeconds;
            }

            return nextTimestampForMining;
        }

        /// <inheritdoc />
        public bool IsValidTimestamp(uint headerUnixTimestamp)
        {
            return (headerUnixTimestamp % this.consensusOptions.TargetSpacingSeconds) == 0;
        }

        public TimeSpan GetRoundLength(int? federationMembersCount)
        {
            federationMembersCount = federationMembersCount ?? this.federationManager.GetFederationMembers().Count;

            uint roundLength = (uint)(federationMembersCount * this.consensusOptions.TargetSpacingSeconds);

            return TimeSpan.FromSeconds(roundLength);
        }
    }
}
