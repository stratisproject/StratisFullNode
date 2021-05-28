using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Utils;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public class PollsRepository : IDisposable
    {
        private readonly DBreezeEngine dbreeze;

        private readonly ILogger logger;

        private readonly DBreezeSerializer dBreezeSerializer;

        private readonly ChainIndexer chainIndexer;

        private readonly uint maxInactiveSeconds;

        internal const string DataTable = "DataTable";

        // The accuracy of this information is critical when determining the quorum requirement
        // for poll execution. For overall data integrity we include it into this repository
        // so that the information is committed together/atomically.
        internal const string ActivityTable = "ActivityTable";
        //  - Key   = Member:BlockHeight:BlockHash:Activity
        //  - Value = BlockTime

        private static readonly byte[] RepositoryHighestIndexKey = new byte[0];

        private static readonly byte[] RepositoryTipKey = new byte[] { 0 };

        private readonly object lockObject = new object();

        private int highestPollId;

        public HashHeightPair CurrentTip { get; private set; }

        public PollsRepository(Network network, DataFolder dataFolder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer, ChainIndexer chainIndexer)
            : this(network, dataFolder.PollsPath, loggerFactory, dBreezeSerializer, chainIndexer)
        {
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
            lock (this.lockObject)
            {
                byte[] key = ActivityKey(pubKey, blockHeight, blockHash, activity);
                transaction.Insert<byte[], uint>(ActivityTable, key, time);
                this.lastActivity[pubKey] = (blockHeight, blockHash, time, activity);
            }
        }

        /// <summary>
        /// This method is used to determine which members get counted towards the quorum requirement when deciding whether to execute polls that
        /// add or remove members. It must be accurate to ensure that the federation is accurately determined.
        /// </summary>
        public bool IsMemberInactive(DBreeze.Transactions.Transaction transaction, PubKey pubKey, ChainedHeader tip)
        {
            Guard.Assert(tip.Height <= this.CurrentTip.Height);

            if (this.lastActivity.TryGetValue(pubKey, out (uint blockHeight, uint256 blockHash, uint blockTime, Activity type) lastActivity))
            {
                if (lastActivity.blockTime <= tip.Header.Time)
                {
                    uint inactiveSeconds = tip.Header.Time - lastActivity.blockTime;
                    if (inactiveSeconds <= this.maxInactiveSeconds)
                        return false;
                }
            }

            // Look backwards for the most recent activity.
            var startKey = this.ActivityKey(pubKey, (uint)tip.Height + 1, 0, (Activity)0);
            var stopKey = this.ActivityKey(pubKey, 0, 0, (Activity)0);
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

                lastActivity = (rowKey.blockHeight, rowKey.blockHash, row.Value, rowKey.activity);
                this.lastActivity[pubKey] = lastActivity;

                uint inactiveSeconds = tip.Header.Time - lastActivity.blockTime;
                return inactiveSeconds > this.maxInactiveSeconds;
            }

            return true;
        }

        public PollsRepository(Network network, string folder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer, ChainIndexer chainIndexer)
        {
            Guard.NotEmpty(folder, nameof(folder));

            Directory.CreateDirectory(folder);
            this.dbreeze = new DBreezeEngine(folder);

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.dBreezeSerializer = dBreezeSerializer;

            this.lastActivity = new Dictionary<PubKey, (uint, uint256, uint, Activity)>();
            this.chainIndexer = chainIndexer;
            this.maxInactiveSeconds = ((PoAConsensusOptions)network.Consensus.Options).FederationMemberMaxIdleTimeSeconds;
        }

        public void Initialize()
        {
            // Load highest index.
            lock (this.lockObject)
            {
                this.highestPollId = -1;

                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    Row<byte[], int> row = transaction.Select<byte[], int>(DataTable, RepositoryHighestIndexKey);

                    if (row.Exists)
                        this.highestPollId = row.Value;

                    Row<byte[], byte[]> rowTip = transaction.Select<byte[], byte[]>(DataTable, RepositoryTipKey);

                    if (rowTip.Exists)
                    {
                        this.CurrentTip = this.dBreezeSerializer.Deserialize<HashHeightPair>(rowTip.Value);
                        if (this.chainIndexer != null && this.chainIndexer.GetHeader(this.CurrentTip.Hash) == null)
                            this.CurrentTip = null;
                    }
                    else
                    {
                        this.CurrentTip = null;
                    }

                    if (this.chainIndexer != null && this.CurrentTip == null)
                    {
                        // This is required for repositories that don't have a stored tip yet.
                        Dictionary<byte[], byte[]> data = transaction.SelectDictionary<byte[], byte[]>(DataTable);

                        var polls = data
                            .Where(d => d.Key.Length == 4)
                            .Select(d => this.dBreezeSerializer.Deserialize<Poll>(d.Value))
                            .Where(p => p.Id <= this.highestPollId && this.chainIndexer.GetHeader(p.PollStartBlockData.Hash) != null)
                            .ToDictionary(p => p.Id, p => p);

                        if (polls.Count > 0)
                        {
                            int maxStartHeight = polls.Max(p => p.Value.PollStartBlockData.Height);
                            int maxVotedInFavorHeight = polls.Max(p => p.Value.PollVotedInFavorBlockData?.Height ?? 0);
                            int maxPollExecutedHeight = polls.Max(p => p.Value.PollExecutedBlockData?.Height ?? 0);
                            int maxHeight = Math.Max(maxStartHeight, Math.Max(maxVotedInFavorHeight, maxPollExecutedHeight));
                            this.CurrentTip = polls.FirstOrDefault(p => p.Value.PollStartBlockData.Height == maxHeight).Value.PollStartBlockData;
                        }
                    }

                    // Trim polls repository to height.
                    //const int trimHeight = 1424647;
                    const int trimHeight = 1358857;
                    if (this.CurrentTip?.Height > trimHeight)
                    {
                        Dictionary<byte[], byte[]> data = transaction.SelectDictionary<byte[], byte[]>(DataTable);

                        Poll[] polls = data
                            .Where(d => d.Key.Length == 4)
                            .Select(d => this.dBreezeSerializer.Deserialize<Poll>(d.Value))
                            .ToArray();

                        // Determine list of polls to remove completely.
                        Poll[] pollsToRemove = polls.Where(p => p.PollStartBlockData.Height > trimHeight).ToArray();

                        if (pollsToRemove.Length > 0)
                            this.RemovePolls(transaction, pollsToRemove.Select(p => p.Id).ToArray());

                        // Update any polls are executed after the trim height.
                        Poll[] executedPollsToUpdate = polls.Where(p => p.PollExecutedBlockData?.Height > trimHeight).ToArray();
                        foreach (Poll poll in executedPollsToUpdate)
                        {
                            poll.PollExecutedBlockData = null;
                            this.UpdatePoll(transaction, poll);
                        }

                        // Update any polls voted in favor of after the trim height.
                        Poll[] votedInFavorPollsToUpdate = polls.Where(p => p.PollVotedInFavorBlockData?.Height > trimHeight).ToArray();
                        foreach (Poll poll in votedInFavorPollsToUpdate)
                        {
                            poll.PollVotedInFavorBlockData = null;
                            this.UpdatePoll(transaction, poll);
                        }

                        this.SaveCurrentTip(transaction, this.chainIndexer.GetHeader(trimHeight));
                    }
                }
            }

            this.logger.LogDebug("Polls repo initialized with highest id: {0}.", this.highestPollId);
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

        public void SaveCurrentTip(DBreeze.Transactions.Transaction transaction, ChainedHeader tip)
        {
            lock (this.lockObject)
            {
                this.CurrentTip = new HashHeightPair(tip);

                if (transaction == null)
                    return;

                transaction.Insert<byte[], byte[]>(DataTable, RepositoryTipKey, this.dBreezeSerializer.Serialize(this.CurrentTip));
            }
        }

        /// <summary>Provides Id of the most recently added poll.</summary>
        public int GetHighestPollId()
        {
            return this.highestPollId;
        }

        public void Synchronous(Action action)
        {
            lock (this.lockObject)
            {
                action();
            }
        }

        private void SaveHighestPollId(DBreeze.Transactions.Transaction transaction)
        {
            if (this.CurrentTip == null)
                transaction.RemoveKey<byte[]>(DataTable, RepositoryTipKey);
            else
                transaction.Insert<byte[], byte[]>(DataTable, RepositoryTipKey, this.dBreezeSerializer.Serialize(this.CurrentTip));

            transaction.Insert<byte[], int>(DataTable, RepositoryHighestIndexKey, this.highestPollId);
        }

        /// <summary>Removes polls for the provided ids.</summary>
        public void DeletePollsAndSetHighestPollId(DBreeze.Transactions.Transaction transaction, params int[] ids)
        {
            lock (this.lockObject)
            {
                foreach (int pollId in ids.OrderBy(a => a))
                {
                    transaction.RemoveKey<byte[]>(DataTable, pollId.ToBytes());
                }

                List<Poll> polls = GetAllPolls(transaction);
                this.highestPollId = (polls.Count == 0) ? -1 : polls.Max(a => a.Id);
                SaveHighestPollId(transaction);
            }
        }

        /// <summary>Removes polls under provided ids.</summary>
        public void RemovePolls(DBreeze.Transactions.Transaction transaction, params int[] ids)
        {
            lock (this.lockObject)
            {
                foreach (int pollId in ids.OrderBy(id => id).Reverse())
                {
                    if (this.highestPollId != pollId)
                        throw new ArgumentException("Only deletion of the most recent item is allowed!");

                    transaction.RemoveKey<byte[]>(DataTable, pollId.ToBytes());

                    this.highestPollId--;
                    this.SaveHighestPollId(transaction);
                }
            }
        }

        public DBreeze.Transactions.Transaction GetTransaction()
        {
            var transaction = this.dbreeze.GetTransaction();

            return transaction;
        }

        /// <summary>Adds new poll.</summary>
        public void AddPolls(DBreeze.Transactions.Transaction transaction, params Poll[] polls)
        {
            lock (this.lockObject)
            {
                foreach (Poll pollToAdd in polls.OrderBy(p => p.Id))
                {
                    if (pollToAdd.Id != this.highestPollId + 1)
                        throw new ArgumentException("Id is incorrect. Gaps are not allowed.");

                    byte[] bytes = this.dBreezeSerializer.Serialize(pollToAdd);

                    transaction.Insert<byte[], byte[]>(DataTable, pollToAdd.Id.ToBytes(), bytes);

                    this.highestPollId++;
                    this.SaveHighestPollId(transaction);
                }
            }
        }

        /// <summary>Updates existing poll.</summary>
        public void UpdatePoll(DBreeze.Transactions.Transaction transaction, Poll poll)
        {
            lock (this.lockObject)
            {
                byte[] bytes = this.dBreezeSerializer.Serialize(poll);

                transaction.Insert<byte[], byte[]>(DataTable, poll.Id.ToBytes(), bytes);
            }
        }

        /// <summary>Loads polls under provided keys from the database.</summary>
        public List<Poll> GetPolls(DBreeze.Transactions.Transaction transaction, params int[] ids)
        {
            lock (this.lockObject)
            {
                var polls = new List<Poll>(ids.Length);

                foreach (int id in ids)
                {
                    Row<byte[], byte[]> row = transaction.Select<byte[], byte[]>(DataTable, id.ToBytes());

                    if (!row.Exists)
                        throw new ArgumentException("Value under provided key doesn't exist!");

                    Poll poll = this.dBreezeSerializer.Deserialize<Poll>(row.Value);

                    polls.Add(poll);
                }

                return polls;
            }
        }

        /// <summary>Loads all polls from the database.</summary>
        public List<Poll> GetAllPolls(DBreeze.Transactions.Transaction transaction)
        {
            lock (this.lockObject)
            {
                var polls = new List<Poll>(this.highestPollId + 1);

                for (int i = 0; i < this.highestPollId + 1; i++)
                {
                    Row<byte[], byte[]> row = transaction.Select<byte[], byte[]>(DataTable, i.ToBytes());

                    if (row.Exists)
                    {
                        Poll poll = this.dBreezeSerializer.Deserialize<Poll>(row.Value);
                        polls.Add(poll);
                    }
                }

                return polls;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.dbreeze.Dispose();
        }
    }
}
