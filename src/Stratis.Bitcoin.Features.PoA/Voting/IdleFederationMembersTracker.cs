using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DBreeze.DataTypes;
using DBreeze.Utils;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public class IdleFederationMembersTracker
    {
        private readonly Network network;
        private readonly PollsRepository pollsRepository;
        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly uint maxInactiveSeconds;

        // The accuracy of this information is critical when determining the quorum requirement
        // for poll execution. For overall data integrity we include it into this repository
        // so that the information is committed together/atomically.
        internal const string ActivityTable = "ActivityTable";
        //  - Key   = Member:BlockHeight:BlockHash:Activity
        //  - Value = BlockTime

        public IdleFederationMembersTracker(Network network, PollsRepository pollsRepository, DBreezeSerializer dBreezeSerializer)
        {
            this.network = network;
            this.pollsRepository = pollsRepository;
            this.dBreezeSerializer = dBreezeSerializer;
            this.maxInactiveSeconds = ((PoAConsensusOptions)network.Consensus.Options).FederationMemberMaxIdleTimeSeconds;
            this.lastActivity = new Dictionary<PubKey, (uint, uint256, uint, Activity)>();
        }

        // Most recent "Joined" or "Mined" activity.
        private Dictionary<PubKey, (uint, uint256, uint, Activity)> lastActivity;

        public enum Activity
        {
            Joined = 0,
            Mined = 1
        };

        private byte[] ActivityKey(PubKey pubKey, uint blockHeight, uint256 blockHash, Activity activity)
        {
            byte[] key = this.dBreezeSerializer.Serialize(pubKey)
                // This has to be big-endian to support ordering by the overall key.
                .Concat(blockHeight.ToBytes())
                .Concat(this.dBreezeSerializer.Serialize(blockHash))
                .Concat(((uint)activity).ToBytes())
                .ToArray();

            return key;
        }

        /// <summary>
        /// It's easier to rewind "last active" information if it's recorded in tables.
        /// </summary>
        public void RecordActivity(DBreeze.Transactions.Transaction transaction, PubKey pubKey, uint blockHeight, uint256 blockHash, Activity activity, uint time)
        {
            byte[] key = ActivityKey(pubKey, blockHeight, blockHash, activity);
            transaction.Insert<byte[], uint>(ActivityTable, key, time);
            this.lastActivity[pubKey] = (blockHeight, blockHash, time, activity);
        }

        public bool TryGetLastCachedActivity(PubKey pubKey, out (uint blockHeight, uint256 blockHash, uint blockTime, Activity type) lastActivity)
        {
            return this.lastActivity.TryGetValue(pubKey, out lastActivity);
        }

        /// <summary>
        /// This method is used to determine which members get counted towards the quorum requirement when deciding whether to execute polls that
        /// add or remove members. It must be accurate to ensure that the federation is accurately determined.
        /// </summary>
        public bool IsMemberInactive(DBreeze.Transactions.Transaction transaction, IFederationMember federationMember, ChainedHeader tip)
        {
            // TODO: Track activity tips separately.
            Guard.Assert(tip.Height <= this.pollsRepository.CurrentTip.Height);

            PubKey pubKey = federationMember.PubKey;
            uint inactiveSeconds;

            if (this.lastActivity.TryGetValue(pubKey, out (uint blockHeight, uint256 blockHash, uint blockTime, Activity type) lastActivity))
            {
                if (lastActivity.blockTime <= tip.Header.Time)
                {
                    inactiveSeconds = tip.Header.Time - lastActivity.blockTime;
                    if (inactiveSeconds <= this.maxInactiveSeconds)
                        return false;
                }
            }

            if (!TryGetLastActivity(transaction, federationMember, tip, out lastActivity))
                return true;

            this.lastActivity[pubKey] = lastActivity;
            inactiveSeconds = tip.Header.Time - lastActivity.blockTime;

            return inactiveSeconds > this.maxInactiveSeconds;
        }

        private (PubKey pubKey, uint blockHeight, uint256 blockHash, Activity activity) DeserializeActivityRowKey(byte[] rowKey)
        {
            (PubKey pubKey, uint blockHeight, uint256 blockHash, Activity activity) key = (null, default(uint), default(uint256), default(Activity));

            var s = new BitcoinStream(new MemoryStream(rowKey), false);
            byte[] pubKeyBytes = new byte[33];
            s.ReadWrite(ref pubKeyBytes);
            key.pubKey = new PubKey(pubKeyBytes);
            byte[] blockHeightBytes = new byte[4];
            s.ReadWrite(ref blockHeightBytes);
            key.blockHeight = BitConverter.ToUInt32(blockHeightBytes.Reverse());
            s.ReadWrite(ref key.blockHash);
            byte[] activityBytes = new byte[4];
            s.ReadWrite(ref activityBytes);
            key.activity = (Activity)BitConverter.ToUInt32(activityBytes.Reverse());

            return key;
        }

        public bool TryGetLastActivity(DBreeze.Transactions.Transaction transaction, IFederationMember federationMember, ChainedHeader tip, out (uint blockHeight, uint256 blockHash, uint blockTime, Activity activity) activity)
        {
            // Look backwards for the most recent activity.
            var startKey = this.ActivityKey(federationMember.PubKey, (uint)tip.Height + 1, 0, (Activity)0);
            var stopKey = this.ActivityKey(federationMember.PubKey, 0, 0, (Activity)0);
            foreach (Row<byte[], uint> row in transaction.SelectBackwardFromTo<byte[], uint>(ActivityTable, startKey, false, stopKey, true))
            {
                if (!row.Exists)
                    continue;

                var rowKey = DeserializeActivityRowKey(row.Key);

                if (rowKey.activity != Activity.Joined && rowKey.activity != Activity.Mined)
                    continue;

                // Check that the block hash is in the consensus chain.
                if (tip.Height < rowKey.blockHeight || tip.GetAncestor((int)rowKey.blockHeight)?.HashBlock != rowKey.blockHash)
                    continue;

                activity = (rowKey.blockHeight, rowKey.blockHash, row.Value, rowKey.activity);
                return true;
            }

            (HashHeightPair block, uint time) joinedTime = (federationMember.JoinedTime != default) ? federationMember.JoinedTime : (new HashHeightPair(this.network.GenesisHash, 0), this.network.GenesisTime);

            if (joinedTime.block.Height <= tip.Height)
            {
                activity = ((uint)(joinedTime.block.Height), joinedTime.block.Hash, joinedTime.time, Activity.Joined);
                return true;
            }

            activity = default;
            return false;
        }
    }
}
