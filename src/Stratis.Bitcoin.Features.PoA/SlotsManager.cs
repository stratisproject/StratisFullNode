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

        private readonly ChainIndexer chainIndexer;

        private readonly ILogger logger;

        public SlotsManager(Network network, IFederationManager federationManager, ChainIndexer chainIndexer, ILoggerFactory loggerFactory)
        {
            Guard.NotNull(network, nameof(network));
            this.federationManager = Guard.NotNull(federationManager, nameof(federationManager));
            this.chainIndexer = chainIndexer;
            this.consensusOptions = (network as PoANetwork).ConsensusOptions;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public uint GetMiningTimestamp(uint currentTime)
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
