using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly IFederationHistory federationHistory;
        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly ChainIndexer chainIndexer;
        private readonly uint maxInactiveSeconds;
        private readonly HashSet<PubKey> members;

        // The accuracy of this information is critical when determining the quorum requirement
        // for poll execution. For overall data integrity we include it into the polls repository
        // so that the information is committed together/atomically.
        private const string ActivityTable = "ActivityTable";
        //  - Key   = Member:BlockHeight:BlockHash:Activity
        //  - Value = BlockTime

        public IdleFederationMembersTracker(Network network, PollsRepository pollsRepository, DBreezeSerializer dBreezeSerializer, ChainIndexer chainIndexer, IFederationHistory federationHistory)
        {
            this.network = network;
            this.pollsRepository = pollsRepository;
            this.dBreezeSerializer = dBreezeSerializer;
            this.chainIndexer = chainIndexer;
            this.federationHistory = federationHistory;
            this.maxInactiveSeconds = ((PoAConsensusOptions)network.Consensus.Options).FederationMemberMaxIdleTimeSeconds;
            this.members = new HashSet<PubKey>();
        }

        public void Initialize()
        {
            this.pollsRepository.WithTransaction(transaction =>
            {
                foreach (var member in EnumerateMembers(transaction))
                {
                    this.members.Add(member);
                }
            });
        }

        private IEnumerable<PubKey> EnumerateMembers(DBreeze.Transactions.Transaction transaction)
        {
            for (byte[] pubKey = new byte[33]; ; )
            {
                pubKey = transaction.SelectForwardSkipFrom<byte[], uint>(ActivityTable, ActivityKey(pubKey, 0, 0, 0), 0)
                    .Where(x => x.Exists).Select(x => x.Key.SafeSubarray(0, 33)).FirstOrDefault();

                if (pubKey == null)
                    break;
                else
                    yield return new PubKey(pubKey);

                for (int i = pubKey.Length - 1; i >= 0; i--)
                {
                    pubKey[i]++;
                    if (pubKey[i] != 0)
                        break;
                }
            }
        }
        
        private byte[] ActivityKey(byte[] pubKey, uint blockHeight, uint256 blockHash, Activity activity)
        {
            // All items must be big-endian to support ordering by the overall key.
            return pubKey.Concat(blockHeight.ToBytes()).Concat(this.dBreezeSerializer.Serialize(blockHash)).Concat(((uint)activity).ToBytes()).ToArray();
        }

        private byte[] ActivityKey(PubKey pubKey, uint blockHeight, uint256 blockHash, Activity activity)
        {
            return ActivityKey(this.dBreezeSerializer.Serialize(pubKey), blockHeight, blockHash, activity);
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

        private void RecordActivity(DBreeze.Transactions.Transaction transaction, PubKey pubKey, ChainedHeader chainedHeader, byte[] key)
        {
            transaction.Insert(ActivityTable, key, chainedHeader.Header.Time);
            this.members.Add(pubKey);
        }

        private void RecordActivity(DBreeze.Transactions.Transaction transaction, PubKey pubKey, ChainedHeader chainedHeader, Activity activity)
        {
            this.RecordActivity(transaction, pubKey, chainedHeader, this.ActivityKey(pubKey, (uint)chainedHeader.Height, chainedHeader.HashBlock, activity));
        }

        private bool TryGetLastActivity(DBreeze.Transactions.Transaction transaction,  IFederationMember federationMember, ChainedHeader tip, out (uint blockHeight, uint256 blockHash, uint blockTime, Activity activity) activity)
        {
            // Look backwards for the most recent activity.
            var startKey = this.ActivityKey(federationMember.PubKey, (uint)tip.Height + 1, 0, 0);
            var stopKey = this.ActivityKey(federationMember.PubKey, 0, 0, 0);
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

        public enum Activity
        {
            Joined = 0,
            Mined = 1
        };

        /// <summary>
        /// Used to confirm that a range of blocks is present in the tracking database.
        /// </summary>
        public class Cursor
        {
            private readonly IdleFederationMembersTracker idleFederationMembersTracker;

            private HashHeightPair FirstConfirmedBlock { get; set; }
            private uint FirstConfirmedTime { get; set; }
            private HashHeightPair LastConfirmedBlock { get; set; }
            private uint LastConfirmedTime { get; set; }

            public Cursor(IdleFederationMembersTracker idleFederationMembersTracker)
            {
                Network network = idleFederationMembersTracker.network;

                this.idleFederationMembersTracker = idleFederationMembersTracker;
                this.FirstConfirmedBlock = new HashHeightPair(network.GenesisHash, 0);
                this.FirstConfirmedTime = network.GenesisTime;
                this.LastConfirmedBlock = new HashHeightPair(network.GenesisHash, 0);
                this.LastConfirmedTime = network.GenesisTime;
                this.lastActivity = new Dictionary<PubKey, (uint, uint256, uint, Activity)>();
            }

            // Most recent "Joined" or "Mined" activity.
            private Dictionary<PubKey, (uint, uint256, uint, Activity)> lastActivity;

            public void RecordActivity(DBreeze.Transactions.Transaction transaction, PubKey pubKey, ChainedHeader chainedHeader, Activity activity)
            {
                this.idleFederationMembersTracker.RecordActivity(transaction, pubKey, chainedHeader, activity);

                uint blockTime = chainedHeader.Header.Time;

                if (this.LastConfirmedBlock.Hash == chainedHeader.Previous.HashBlock)
                {
                    this.LastConfirmedBlock = new HashHeightPair(chainedHeader);
                    this.LastConfirmedTime = blockTime;
                }

                this.lastActivity[pubKey] = ((uint)chainedHeader.Height, chainedHeader.HashBlock, blockTime, activity);
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
                Guard.Assert(tip.Height <= this.idleFederationMembersTracker.pollsRepository.CurrentTip.Height);

                PubKey pubKey = federationMember.PubKey;
                uint inactiveSeconds;

                if (this.lastActivity.TryGetValue(pubKey, out (uint blockHeight, uint256 blockHash, uint blockTime, Activity type) lastActivity))
                {
                    if (lastActivity.blockTime <= tip.Header.Time)
                    {
                        inactiveSeconds = tip.Header.Time - lastActivity.blockTime;
                        if (inactiveSeconds <= this.idleFederationMembersTracker.maxInactiveSeconds)
                            return false;
                    }
                }

                if (!this.TryGetLastActivity(transaction, federationMember, tip, out lastActivity))
                    return true;

                this.lastActivity[pubKey] = lastActivity;
                inactiveSeconds = tip.Header.Time - lastActivity.blockTime;

                return inactiveSeconds > this.idleFederationMembersTracker.maxInactiveSeconds;
            }

            public bool TryGetLastActivity(DBreeze.Transactions.Transaction transaction, IFederationMember federationMember, ChainedHeader tip, out (uint blockHeight, uint256 blockHash, uint blockTime, Activity activity) activity)
            {
                this.EnsureBlocksPresent(transaction, tip);

                return this.idleFederationMembersTracker.TryGetLastActivity(transaction, federationMember, tip, out activity);
            }

            /// <summary>
            /// Ensures that the necessary blocks are present to make an idle determination at the given block.
            /// </summary>
            /// <param name="tip">The block at which to make the "is idle" determination.</param>
            private void EnsureBlocksPresent(DBreeze.Transactions.Transaction transaction, ChainedHeader tip)
            {
                // If the tip and lower bound of interest is contained within the bubble then exit.
                if ((this.FirstConfirmedTime + this.idleFederationMembersTracker.maxInactiveSeconds) < tip.Header.Time && this.LastConfirmedBlock.Height >= tip.Height)
                    return;

                var maxInactiveSeconds = this.idleFederationMembersTracker.maxInactiveSeconds;

                // Determine the last required block. We will read from the tip unless its included in the current bubble, in which case we extend the bubble downwards.
                ChainedHeader lastRequiredBlock = tip;
                if (tip.Height >= this.FirstConfirmedBlock.Height && tip.Height <= this.LastConfirmedBlock.Height)
                    lastRequiredBlock = this.idleFederationMembersTracker.chainIndexer.GetHeader(this.FirstConfirmedBlock.Height).Previous;

                // Determine first required block.
                ChainedHeader firstRequiredBlock = lastRequiredBlock;
                while ((firstRequiredBlock.Header.Time + maxInactiveSeconds) >= tip.Header.Time && firstRequiredBlock.Height > 1)
                {
                    if (firstRequiredBlock.Previous.HashBlock == this.LastConfirmedBlock.Hash && (this.FirstConfirmedTime + maxInactiveSeconds) < tip.Header.Time)
                        break;

                    firstRequiredBlock = firstRequiredBlock.Previous;
                }

                // Determine which blocks are already present by accumulating the blocks in this range mined by each miner.
                int arraySize = lastRequiredBlock.Height - firstRequiredBlock.Height + 1;
                var present = new bool[arraySize];

                foreach (PubKey pubKey in this.idleFederationMembersTracker.members)
                {
                    var pubKeyBytes = this.idleFederationMembersTracker.dBreezeSerializer.Serialize(pubKey);
                    var startKey = this.idleFederationMembersTracker.ActivityKey(pubKeyBytes, (uint)firstRequiredBlock.Height, 0, 0);
                    var stopKey = this.idleFederationMembersTracker.ActivityKey(pubKeyBytes, (uint)lastRequiredBlock.Height + 1, 0, 0);
                    foreach (Row<byte[], uint> row in transaction.SelectForwardFromTo<byte[], uint>(ActivityTable, startKey, false, stopKey, true))
                    {
                        if (!row.Exists)
                            continue;

                        var rowKey = this.idleFederationMembersTracker.DeserializeActivityRowKey(row.Key);

                        if (rowKey.activity != Activity.Mined)
                            continue;

                        present[rowKey.blockHeight - firstRequiredBlock.Height] = true;
                        break;
                    }
                }
                
                // Process the blocks that are not present.
                ChainedHeader[] chainedHeaders = lastRequiredBlock.EnumerateToGenesis()
                    .TakeWhile(x => x.Height >= firstRequiredBlock.Height)
                    .Where(x => !present[x.Height - firstRequiredBlock.Height])
                    .Reverse()
                    .ToArray();

                if (chainedHeaders.Length > 0)
                {
                    foreach ((PubKey pubKey, ChainedHeader chainedHeader, byte[] key) in this.idleFederationMembersTracker.federationHistory.GetFederationMembersForBlocks(chainedHeaders)
                        .Select((member, n) => (member.PubKey, chainedHeaders[n], this.idleFederationMembersTracker.ActivityKey(member.PubKey, (uint)chainedHeaders[n].Height, chainedHeaders[n].HashBlock, Activity.Mined)))
                        .OrderBy(x => x.Item3, new ByteArrayComparer()))
                    {
                        this.idleFederationMembersTracker.RecordActivity(transaction, pubKey, chainedHeader, key);
                    }
                }

                ChainedHeader prevFirst = this.idleFederationMembersTracker.chainIndexer.GetHeader(this.FirstConfirmedBlock.Height).Previous;

                if (firstRequiredBlock.Previous.HashBlock != this.LastConfirmedBlock.Hash)
                {
                    this.FirstConfirmedBlock = new HashHeightPair(firstRequiredBlock.HashBlock, firstRequiredBlock.Height);
                    this.FirstConfirmedTime = firstRequiredBlock.Header.Time;
                }

                if (lastRequiredBlock != prevFirst)
                {
                    this.LastConfirmedBlock = new HashHeightPair(lastRequiredBlock.HashBlock, lastRequiredBlock.Height);
                    this.LastConfirmedTime = lastRequiredBlock.Header.Time;
                }
            }
        }
    }
}