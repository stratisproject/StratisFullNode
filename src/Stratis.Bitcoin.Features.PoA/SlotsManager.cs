using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
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
        uint GetMiningTimestamp(uint currentTime);

        /// <summary>Determines whether timestamp is valid according to the network rules.</summary>
        bool IsValidTimestamp(uint headerUnixTimestamp);

        TimeSpan GetRoundLength(int? federationMembersCount = null);
    }

    public class SlotsManager : ISlotsManager
    {
        private readonly PoAConsensusOptions consensusOptions;

        private readonly IFederationManager federationManager;

        private readonly IFederationHistory federationHistory;

        private readonly ChainIndexer chainIndexer;

        private readonly ILogger logger;

        public SlotsManager(Network network, IFederationManager federationManager, IFederationHistory federationHistory, ChainIndexer chainIndexer, ILoggerFactory loggerFactory)
        {
            Guard.NotNull(network, nameof(network));
            this.federationManager = Guard.NotNull(federationManager, nameof(federationManager));
            this.federationHistory = federationHistory;
            this.chainIndexer = chainIndexer;
            this.consensusOptions = (network as PoANetwork).ConsensusOptions;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        public uint GetMiningTimestamp(uint currentTime)
        {
            /*
            A miner can calculate when its expected to mine by looking at the ordered list of federation members
            and the last block that was mined and by whom. It can count the number of mining slots from that member
            to itself and multiply that with the target spacing to arrive at its mining timestamp.
            The fact that the federation can change at any time adds extra complexity to this basic approach. 
            The miner that mined the last block may no longer exist when the next block is about to be mined. As such
            we may need to look a bit further back to find a "reference miner" that still occurs in the latest federation.
            */
            ChainedHeader tip = this.chainIndexer.Tip;
            if (tip.Height < this.consensusOptions.GetMiningTimestampV2ActivationHeight)
                return GetMiningTimestampLegacy(currentTime);

            List<IFederationMember> federationMembers = this.federationHistory.GetFederationForBlock(tip, 1);
            if (federationMembers == null)
                throw new Exception($"Could not determine the federation at block { tip.Height } + 1.");

            int myIndex = federationMembers.FindIndex(m => m.PubKey == this.federationManager.CurrentFederationKey?.PubKey);
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

        /// <inheritdoc />
        public uint GetMiningTimestampLegacy(uint currentTime)
        {
            if (!this.federationManager.IsFederationMember)
                throw new NotAFederationMemberException();

            List<IFederationMember> federationMembers = this.federationManager.GetFederationMembers();

            // Round length in seconds.
            uint roundTime = (uint)this.GetRoundLength(federationMembers.Count).TotalSeconds;

            // Index of a slot that current node can take in each round.
            uint slotIndex = (uint)federationMembers.FindIndex(x => x.PubKey == this.federationManager.CurrentFederationKey.PubKey);

            // Time when current round started.
            uint roundStartTimestamp = (currentTime / roundTime) * roundTime;
            uint nextTimestampForMining = roundStartTimestamp + slotIndex * this.consensusOptions.TargetSpacingSeconds;

            // Check if we have missed our turn for this round.
            // We still consider ourselves "in a turn" if we are in the first half of the turn and we haven't mined there yet.
            // This might happen when starting the node for the first time or if there was a problem when mining.
            if (currentTime > nextTimestampForMining + (this.consensusOptions.TargetSpacingSeconds / 2) // We are closer to the next turn than our own
                  || this.chainIndexer.Tip.Header.Time == nextTimestampForMining) // We have already mined in that slot
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
