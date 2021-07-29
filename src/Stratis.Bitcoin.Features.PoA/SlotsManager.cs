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
            */
            List<IFederationMember> federationMembersAtMinedBlock = this.federationHistory.GetFederationForBlock(tip, 1);
            if (federationMembersAtMinedBlock == null)
                throw new Exception($"Could not determine the federation at block { tip.Height } + 1.");

            int myIndex = federationMembersAtMinedBlock.FindIndex(m => m.PubKey == this.federationManager.CurrentFederationKey?.PubKey);
            if (myIndex < 0)
                throw new NotAFederationMemberException();

            var roundTime = this.GetRoundLength(federationMembersAtMinedBlock.Count);

            // Determine the index of the miner that mined the last block.
            IFederationMember lastMiner = this.federationHistory.GetFederationMemberForBlock(tip);
            List<IFederationMember> federationMembersAtTip = this.federationHistory.GetFederationForBlock(tip);
            int lastMinerIndex = federationMembersAtTip.FindIndex(m => m.PubKey == lastMiner.PubKey);
            if (lastMinerIndex < 0)
                throw new Exception($"The miner ('{lastMiner.PubKey.ToHex()}') of the block at height {tip.Height} could not be located in federation.");

            int index = -1;

            // Looking back, find the first member in common between the old and new federation.
            for (int i = federationMembersAtTip.Count; i >= 1 && index < 0; i--)
            {
                PubKey keyToFind = federationMembersAtTip[(i + lastMinerIndex) % federationMembersAtTip.Count].PubKey;
                index = federationMembersAtMinedBlock.FindIndex(m => m.PubKey == keyToFind);
            }
            
            if (index < 0)
                throw new Exception("Could not find a member in common between the old and new federation");

            // Found a member that occurs in both old and new federation.
            // Determine "distance" apart in new federation.
            int diff = myIndex - index;
            while (diff < 0)
                diff += federationMembersAtMinedBlock.Count;

            DateTimeOffset tipTime = tip.Header.BlockTime;
            while ((tipTime + roundTime) < timeNow)
                tipTime += roundTime;

            DateTimeOffset timeToMine = tipTime + roundTime * diff / federationMembersAtMinedBlock.Count;
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
