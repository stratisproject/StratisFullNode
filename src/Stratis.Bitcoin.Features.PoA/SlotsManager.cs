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
        DateTimeOffset GetMiningTimestamp(ChainedHeader tip, DateTimeOffset timeNow);

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
            this.federationHistory = Guard.NotNull(federationHistory, nameof(federationHistory));
            this.chainIndexer = chainIndexer;
            this.consensusOptions = (network as PoANetwork).ConsensusOptions;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public DateTimeOffset GetMiningTimestamp(ChainedHeader tip, DateTimeOffset timeNow)
        {
            /*
            A miner can calculate when its expected to mine by looking at the ordered list of federation members
            and the last block that was mined and by whom. It can count the number of mining slots from that member
            to itself and multiply that with the target spacing to arrive at its mining timestamp.
            */
            List<IFederationMember> federationMembers = this.federationHistory.GetFederationForBlock(tip, true);

            int myIndex = federationMembers.FindIndex(m => m.PubKey == this.federationManager.CurrentFederationKey?.PubKey);
            if (myIndex < 0)
                throw new NotAFederationMemberException();

            var roundTime = this.GetRoundLength(federationMembers.Count);

            // Determine the index of the miner that mined the last block.
            IFederationMember lastMiner = this.federationHistory.GetFederationMemberForBlock(tip);
            List<IFederationMember> oldFederationMembers = this.federationHistory.GetFederationForBlock(tip);
            int lastMinerIndex = oldFederationMembers.FindIndex(m => m.PubKey == lastMiner.PubKey);
            if (lastMinerIndex < 0)
                throw new Exception($"The miner ('{lastMiner.PubKey.ToHex()}') of the block at height {tip.Height} could not be located in federation.");

            int index = -1;

            // Looking back, find the first member in common between the old and new federation.
            for (int i = oldFederationMembers.Count; i >= 1 && index < 0; i--)
            {
                PubKey keyToFind = oldFederationMembers[(i + lastMinerIndex) % oldFederationMembers.Count].PubKey;
                index = federationMembers.FindIndex(m => m.PubKey == keyToFind);
            }
            
            if (index < 0)
                throw new Exception("Could not find a member in common between the old and new federation");

            // Found a member that occurs in both old and new federation.
            // Determine "distance" apart in new federation.
            int diff = myIndex - index;
            while (diff < 0)
                diff += federationMembers.Count;

            DateTimeOffset tipTime = tip.Header.BlockTime;
            while ((tipTime + roundTime) < timeNow)
                tipTime += roundTime;

            DateTimeOffset timeToMine = tipTime + roundTime * diff / federationMembers.Count;
            if (timeToMine < timeNow)
                timeToMine += roundTime;

            // Ensure we have not already mined in this round.
            for (ChainedHeader header = tip; header != null; header = header.Previous)
            {
                if ((header.Header.BlockTime + roundTime) <= timeToMine)
                    break;

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
