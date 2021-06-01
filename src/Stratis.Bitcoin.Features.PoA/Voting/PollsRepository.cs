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

        private readonly NodeSettings nodeSettings;

        internal const string DataTable = "DataTable";

        private static readonly byte[] RepositoryTipKey = new byte[] { 0 };

        private readonly object lockObject = new object();

        private int highestPollId;

        public HashHeightPair CurrentTip { get; private set; }

        public PollsRepository(DataFolder dataFolder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer, ChainIndexer chainIndexer, NodeSettings nodeSettings)
            : this(dataFolder.PollsPath, loggerFactory, dBreezeSerializer, chainIndexer, nodeSettings)
        {
        }


        public PollsRepository(string folder, ILoggerFactory loggerFactory, DBreezeSerializer dBreezeSerializer, ChainIndexer chainIndexer, NodeSettings nodeSettings)
        {
            Guard.NotEmpty(folder, nameof(folder));

            Directory.CreateDirectory(folder);
            this.dbreeze = new DBreezeEngine(folder);

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.dBreezeSerializer = dBreezeSerializer;

            this.chainIndexer = chainIndexer;
            this.nodeSettings = nodeSettings;
        }

        public void Initialize()
        {
            // Load highest index.
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    Dictionary<byte[], byte[]> data = transaction.SelectDictionary<byte[], byte[]>(DataTable);

                    Poll[] polls = data
                        .Where(d => d.Key.Length == 4)
                        .Select(d => this.dBreezeSerializer.Deserialize<Poll>(d.Value))
                        .ToArray();

                    this.highestPollId = (polls.Length > 0) ? polls.Max(p => p.Id) : -1;

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

                    int trimHeight = this.chainIndexer.Tip.Height;

                    if (this.nodeSettings != null && this.nodeSettings.ConfigReader.GetOrDefault(PoAFeature.ReconstructFederationFlag, -1) != -1)
                    {
                        trimHeight = this.nodeSettings.ConfigReader.GetOrDefault(PoAFeature.ReconstructFederationFlag, 0);
                    }

                    if (trimHeight > (this.CurrentTip?.Height ?? 0))
                        trimHeight = (this.CurrentTip?.Height ?? 0);

                    int maxStartHeight = polls.Max(p => p.PollStartBlockData.Height);
                    int maxVotedInFavorHeight = polls.Max(p => p.PollVotedInFavorBlockData?.Height ?? 0);
                    int maxPollExecutedHeight = polls.Max(p => p.PollExecutedBlockData?.Height ?? 0);
                    int maxHeight = Math.Max(maxStartHeight, Math.Max(maxVotedInFavorHeight, maxPollExecutedHeight));

                    // Trim polls repository to height.
                    if (maxHeight > trimHeight)
                    {
                        // Determine list of polls to remove completely.
                        Poll[] pollsToRemove = polls.Where(p => p.PollStartBlockData.Height > trimHeight).ToArray();

                        if (pollsToRemove.Length > 0)
                        {
                            this.RemovePolls(transaction, pollsToRemove.Select(p => p.Id).ToArray());

                            // Ensures we don't try to update polls that were removed.
                            data = transaction.SelectDictionary<byte[], byte[]>(DataTable);
                            polls = data
                                .Where(d => d.Key.Length == 4)
                                .Select(d => this.dBreezeSerializer.Deserialize<Poll>(d.Value))
                                .ToArray();

                            Guard.Assert(this.highestPollId == ((polls.Length > 0) ? polls.Max(p => p.Id) : -1));
                        }

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
                        transaction.Commit();
                    }
                }
            }

            this.logger.LogDebug("Polls repo initialized with highest id: {0}.", this.highestPollId);
        }

        public void SaveCurrentTip(DBreeze.Transactions.Transaction transaction, ChainedHeader tip = null)
        {
            lock (this.lockObject)
            {
                if (tip != null)
                    this.CurrentTip = new HashHeightPair(tip);

                if (transaction != null)
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
                }
            }
        }

        public T WithTransaction<T>(Func<DBreeze.Transactions.Transaction, T> func)
        {
            lock (this.lockObject)
            {
                using (var transaction = this.dbreeze.GetTransaction())
                {
                    return func(transaction);
                }
            }
        }

        public void WithTransaction(Action<DBreeze.Transactions.Transaction> action)
        {
            lock (this.lockObject)
            {
                using (var transaction = this.dbreeze.GetTransaction())
                {
                    action(transaction);
                }
            }
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
