using System;
using System.Collections.Generic;
using System.Linq;
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

        uint GetRoundLengthSeconds(int? federationMembersCount = null);
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
            this.federationHistory = Guard.NotNull(federationHistory, nameof(federationHistory));
            this.chainIndexer = chainIndexer;
            this.consensusOptions = (network as PoANetwork).ConsensusOptions;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public uint GetMiningTimestamp(uint currentTime)
        {
            /*
            A miner can calculate when its expected to mine by looking at the ordered list of federation members
            and the last block that was mined and by whom. It can count the number of mining slots from that member
            to itself and multiply that with the target spacing to arrive at its mining timestamp.
            */

            if (!this.federationManager.IsFederationMember)
                throw new NotAFederationMemberException();

            ChainedHeader tip = this.chainIndexer.Tip;
            List<IFederationMember> federationMembers = this.federationHistory.GetFederationForBlock(tip, true);

            // Round length in seconds.
            uint roundTime = this.GetRoundLengthSeconds(federationMembers.Count);

            // Determine when the last round started by looking at who mined the tip.
            IFederationMember lastMiner = this.federationHistory.GetFederationMemberForBlock(tip, federationMembers);
            int lastMinerIndex = federationMembers.FindIndex(m => m.PubKey == lastMiner.PubKey);
            uint lastRoundStart = tip.Header.Time - (uint)(lastMinerIndex * roundTime / federationMembers.Count);

            // To calculate the latest round start bring the last round forward in round time increments but stop short of the current time.
            uint prevRoundStart = lastRoundStart + ((currentTime - lastRoundStart) / roundTime) * roundTime;

            // Add our own slot position to determine our earliest mining timestamp.
            int thisMinerIndex = federationMembers.FindIndex(m => m.PubKey == this.federationManager.CurrentFederationKey.PubKey);
            uint nextTimestampForMining = prevRoundStart + (uint)(thisMinerIndex * roundTime / federationMembers.Count);
            // Start in the past in case we still have to mine that slot.
            if (nextTimestampForMining > currentTime)
                nextTimestampForMining -= roundTime;

            // If the current time is closer to the next mining slot then mine there.
            if (currentTime > nextTimestampForMining + roundTime / 2 || nextTimestampForMining < tip.Header.Time)
                nextTimestampForMining += roundTime;

            // Don't break the round time rule.
            // Look up to "round time" blocks back to find the last block we mined.
            for (ChainedHeader prev = tip; prev != null; prev = prev.Previous)
            {
                if (prev.Header.Time <= (nextTimestampForMining - roundTime))
                    break;

                if (this.federationHistory.GetFederationMemberForBlock(prev, federationMembers)?.PubKey == this.federationManager.CurrentFederationKey.PubKey)
                {
                    nextTimestampForMining += roundTime;
                    break;
                }
            }

            return nextTimestampForMining;
        }

        public uint GetRoundLengthSeconds(int? federationMembersCount = null)
        {
            federationMembersCount = federationMembersCount ?? this.federationManager.GetFederationMembers().Count;

            uint roundLength = (uint)(federationMembersCount * this.consensusOptions.TargetSpacingSeconds);

            return roundLength;
        }
    }
}
