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

        internal const string TableName = "DataTable";

        private static readonly byte[] RepositoryHighestIndexKey = new byte[0];

        private static readonly byte[] RepositoryTipKey = new byte[] { 0 };

        private readonly object lockObject = new object();

        private int highestPollId;

        public HashHeightPair CurrentTip { get; private set; }

        public PollsRepository(DataFolder dataFolder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer, ChainIndexer chainIndexer)
            : this(dataFolder.PollsPath, loggerFactory, dBreezeSerializer, chainIndexer)
        {
        }

        public PollsRepository(string folder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer, ChainIndexer chainIndexer)
        {
            Guard.NotEmpty(folder, nameof(folder));

            Directory.CreateDirectory(folder);
            this.dbreeze = new DBreezeEngine(folder);

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.dBreezeSerializer = dBreezeSerializer;
            this.chainIndexer = chainIndexer;
        }

        public void Initialize()
        {
            // Load highest index.
            lock (this.lockObject)
            {
                this.highestPollId = -1;

                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    Row<byte[], int> row = transaction.Select<byte[], int>(TableName, RepositoryHighestIndexKey);

                    if (row.Exists)
                        this.highestPollId = row.Value;

                    Row<byte[], byte[]> rowTip = transaction.Select<byte[], byte[]>(TableName, RepositoryTipKey);

                    if (rowTip.Exists)
                        this.CurrentTip = this.dBreezeSerializer.Deserialize<HashHeightPair>(rowTip.Value);
                    else if (this.chainIndexer != null)
                    {
                        // This is required for repositories that don't have a stored tip yet.
                        Dictionary<byte[], byte[]> data = transaction.SelectDictionary<byte[], byte[]>(TableName);

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
                }
            }

            this.logger.LogDebug("Polls repo initialized with highest id: {0}.", this.highestPollId);
        }

        public void SaveCurrentTip(ChainedHeader tip, bool updateToDb = true)
        {
            lock (this.lockObject)
            {
                this.CurrentTip = new HashHeightPair(tip);

                if (!updateToDb)
                    return;

                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    transaction.Insert<byte[], byte[]>(TableName, RepositoryTipKey, this.dBreezeSerializer.Serialize(this.CurrentTip));
                    transaction.Commit();
                }
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
                transaction.RemoveKey<byte[]>(TableName, RepositoryTipKey);
            else
                transaction.Insert<byte[], byte[]>(TableName, RepositoryTipKey, this.dBreezeSerializer.Serialize(this.CurrentTip));

            transaction.Insert<byte[], int>(TableName, RepositoryHighestIndexKey, this.highestPollId);
        }

        /// <summary>Removes polls for the provided ids.</summary>
        public void DeletePollsAndSetHighestPollId(params int[] ids)
        {
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    foreach (int pollId in ids.OrderBy(a => a))
                    {
                        transaction.RemoveKey<byte[]>(TableName, pollId.ToBytes());
                    }

                    transaction.Commit();
                }

                List<Poll> polls = GetAllPolls();
                this.highestPollId = (polls.Count == 0) ? -1 : polls.Max(a => a.Id);
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    SaveHighestPollId(transaction);
                    transaction.Commit();
                }
            }
        }

        /// <summary>Removes polls under provided ids.</summary>
        public void RemovePolls(params int[] ids)
        {
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    foreach (int pollId in ids.OrderBy(id => id).Reverse())
                    {
                        if (this.highestPollId != pollId)
                            throw new ArgumentException("Only deletion of the most recent item is allowed!");

                        transaction.RemoveKey<byte[]>(TableName, pollId.ToBytes());

                        this.highestPollId--;
                        this.SaveHighestPollId(transaction);
                    }

                    transaction.Commit();
                }
            }
        }

        /// <summary>Adds new poll.</summary>
        public void AddPolls(params Poll[] polls)
        {
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    foreach (Poll pollToAdd in polls.OrderBy(p => p.Id))
                    {
                        if (pollToAdd.Id != this.highestPollId + 1)
                            throw new ArgumentException("Id is incorrect. Gaps are not allowed.");

                        byte[] bytes = this.dBreezeSerializer.Serialize(pollToAdd);

                        transaction.Insert<byte[], byte[]>(TableName, pollToAdd.Id.ToBytes(), bytes);

                        this.highestPollId++;
                        this.SaveHighestPollId(transaction);
                    }

                    transaction.Commit();
                }
            }
        }

        /// <summary>Updates existing poll.</summary>
        public void UpdatePoll(Poll poll)
        {
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    Row<byte[], byte[]> row = transaction.Select<byte[], byte[]>(TableName, poll.Id.ToBytes());

                    if (!row.Exists)
                        throw new ArgumentException("Value doesn't exist!");

                    byte[] bytes = this.dBreezeSerializer.Serialize(poll);

                    transaction.Insert<byte[], byte[]>(TableName, poll.Id.ToBytes(), bytes);

                    transaction.Commit();
                }
            }
        }

        /// <summary>Loads polls under provided keys from the database.</summary>
        public List<Poll> GetPolls(params int[] ids)
        {
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    var polls = new List<Poll>(ids.Length);

                    foreach (int id in ids)
                    {
                        Row<byte[], byte[]> row = transaction.Select<byte[], byte[]>(TableName, id.ToBytes());

                        if (!row.Exists)
                            throw new ArgumentException("Value under provided key doesn't exist!");

                        Poll poll = this.dBreezeSerializer.Deserialize<Poll>(row.Value);

                        polls.Add(poll);
                    }

                    return polls;
                }
            }
        }

        /// <summary>Loads all polls from the database.</summary>
        public List<Poll> GetAllPolls()
        {
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    var polls = new List<Poll>(this.highestPollId + 1);

                    for (int i = 0; i < this.highestPollId + 1; i++)
                    {
                        Row<byte[], byte[]> row = transaction.Select<byte[], byte[]>(TableName, i.ToBytes());

                        if (row.Exists)
                        {
                            Poll poll = this.dBreezeSerializer.Deserialize<Poll>(row.Value);
                            polls.Add(poll);
                        }
                    }

                    return polls;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.dbreeze.Dispose();
        }
    }
}
