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
        DateTimeOffset GetMiningTimestamp(ChainedHeader tip, DateTimeOffset currentTime);

        TimeSpan GetRoundLength(int? federationMembersCount = null);
    }

    public class SlotsManager : ISlotsManager
    {
        private readonly PoAConsensusOptions consensusOptions;

        private readonly IFederationManager federationManager;

        private readonly IFederationHistory federationHistory;

        public SlotsManager(Network network, IFederationManager federationManager, IFederationHistory federationHistory)
        {
            Guard.NotNull(network, nameof(network));
            this.federationManager = Guard.NotNull(federationManager, nameof(federationManager));
            this.federationHistory = Guard.NotNull(federationHistory, nameof(federationHistory));
            this.consensusOptions = (network as PoANetwork).ConsensusOptions;
        }

        /// <inheritdoc />
        public DateTimeOffset GetMiningTimestamp(ChainedHeader tip, DateTimeOffset timeNow)
        {
            /*
            A miner can calculate when its expected to mine by looking at the ordered list of federation members
            and the last block that was mined and by whom. It can count the number of mining slots from that member
            to itself and multiply that with the target spacing to arrive at its mining timestamp.

            The fact that the federation can change at any time adds a little complexity to this basic approach. 
            The miner that mined the last block may no longer exist when the next block is about to be mined. As such
            we may need to look a bit further back to find a "reference miner" that still occurs in the latest federation.
             */
            List<IFederationMember> federationMembersAtMinedBlock = this.federationHistory.GetFederationForBlock(tip, 1);
            if (federationMembersAtMinedBlock == null)
                throw new Exception($"Could not determine the federation at block { tip.Height } + 1.");

            int myIndex = federationMembersAtMinedBlock.FindIndex(m => m.PubKey == this.federationManager.CurrentFederationKey?.PubKey);
            if (myIndex < 0)
                throw new NotAFederationMemberException();

            // Find a "reference miner" to determine our slot against.
            ChainedHeader referenceMinerBlock = tip;
            IFederationMember referenceMiner = null;
            int referenceMinerIndex = -1;
            int referenceMinerDepth = 0;
            for (int i = 0; i < federationMembersAtMinedBlock.Count; i++, referenceMinerDepth++)
            {
                referenceMiner = this.federationHistory.GetFederationMemberForBlock(referenceMinerBlock);
                referenceMinerIndex = federationMembersAtMinedBlock.FindIndex(m => m.PubKey == referenceMiner.PubKey);
                if (referenceMinerIndex >= 0)
                    break;
            }

            if (referenceMinerIndex < 0)
                throw new Exception("Could not find a member in common between the old and new federation");

            // Found a reference miner that also occurs in the latest federation.
            // Determine how many blocks before our mining slot.
            int blocksFromTipToMiningSlot = myIndex - referenceMinerIndex - referenceMinerDepth;
            while (blocksFromTipToMiningSlot < 0)
                blocksFromTipToMiningSlot += federationMembersAtMinedBlock.Count;

            var roundTime = this.GetRoundLength(federationMembersAtMinedBlock.Count);

            // Advance the tip time for any rounds that may not have been mined.
            DateTimeOffset tipTime = tip.Header.BlockTime;
            while ((tipTime + roundTime) < timeNow)
                tipTime += roundTime;

            // Determine the time to mine by adding a fraction of the round time according to how far our slots is "into" the round.
            DateTimeOffset timeToMine = tipTime + roundTime * blocksFromTipToMiningSlot / federationMembersAtMinedBlock.Count;
            if (timeToMine < timeNow)
                timeToMine += roundTime;

            // Ensure we have not already mined in this round.
            // The loop tries to find a block (mined by us) within "roundTime" of "timeToMine". 
            // If found the timeToMine is bumped by one round.
            for (ChainedHeader header = tip; header != null; header = header.Previous)
            {
                // If the current block is to far back in time to meeting our criteria the exit.
                if ((header.Header.BlockTime + roundTime) <= timeToMine)
                    break;

                // A recent block was found. Postpone mining to the next round.
                if (this.federationHistory.GetFederationMemberForBlock(header).PubKey == this.federationManager.CurrentFederationKey.PubKey)
                {
                    timeToMine += roundTime;
                    break;
                }
            }

            return timeToMine;
        }

        public TimeSpan GetRoundLength(int? federationMembersCount)
        {
            federationMembersCount = federationMembersCount ?? this.federationManager.GetFederationMembers().Count;

            uint roundLength = (uint)(federationMembersCount * this.consensusOptions.TargetSpacingSeconds);

            return TimeSpan.FromSeconds(roundLength);
        }
    }
}
