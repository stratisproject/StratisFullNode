﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Utils;
using NBitcoin;
using NLog;
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

        internal const string DataTable = "DataTable";

        private static readonly byte[] RepositoryTipKey = new byte[] { 0 };

        private readonly object lockObject = new object();

        private int highestPollId;

        public HashHeightPair CurrentTip { get; private set; }

        public PollsRepository(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer, ChainIndexer chainIndexer)
            : this(dataFolder.PollsPath, dBreezeSerializer, chainIndexer)
        {
        }


        public PollsRepository(string folder, DBreezeSerializer dBreezeSerializer, ChainIndexer chainIndexer)
        {
            Guard.NotEmpty(folder, nameof(folder));

            Directory.CreateDirectory(folder);
            this.dbreeze = new DBreezeEngine(folder);

            this.logger = LogManager.GetCurrentClassLogger();
            this.dBreezeSerializer = dBreezeSerializer;

            this.chainIndexer = chainIndexer;
        }

        public void Initialize()
        {
            // Load highest index.
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    try
                    {
                        List<Poll> polls = GetAllPolls(transaction);

                        // If the polls repository contains duplicate polls then reset the highest poll id and 
                        // set the tip to null.
                        // This will trigger the VotingManager to rebuild the voting and polls repository as the
                        // polls repository tip is null. This happens later during startup, see VotingManager.Synchronize()
                        var uniquePolls = new HashSet<Poll>(polls);
                        if (uniquePolls.Count != polls.Count)
                        {
                            this.logger.Warn("The polls repo contains {0} duplicate polls. Will rebuild it.", polls.Count - uniquePolls.Count);

                            this.ResetLocked(transaction);
                            transaction.Commit();
                            return;
                        }

                        Row<byte[], byte[]> rowTip = transaction.Select<byte[], byte[]>(DataTable, RepositoryTipKey);
                        if (!rowTip.Exists)
                        {
                            this.logger.Info("The polls repository tip is unknown. Will re-build the repo.");
                            this.ResetLocked(transaction);
                            transaction.Commit();
                            return;
                        }

                        this.CurrentTip = this.dBreezeSerializer.Deserialize<HashHeightPair>(rowTip.Value);
                        if (this.chainIndexer == null || this.chainIndexer.GetHeader(this.CurrentTip.Hash) != null)
                        {
                            this.highestPollId = (polls.Count > 0) ? polls.Max(p => p.Id) : -1;
                            return;
                        }

                        this.logger.Info("The polls repository tip {0} was not found in the consensus chain. Determining fork.", this.CurrentTip);

                        // == Find fork.
                        // The polls repository tip could not be found in the consenus chain.
                        // Look at all other known hash/height pairs to find something in common with the consensus chain.
                        // We will take that as the last known valid height.

                        int maxGoodHeight = -1;

                        foreach (Poll poll in polls)
                        {
                            if (poll.PollStartBlockData.Height > maxGoodHeight && this.chainIndexer.GetHeader(poll.PollStartBlockData.Hash) != null)
                                maxGoodHeight = poll.PollStartBlockData.Height;
                            if (poll.PollExecutedBlockData?.Height > maxGoodHeight && this.chainIndexer.GetHeader(poll.PollExecutedBlockData.Hash) != null)
                                maxGoodHeight = poll.PollExecutedBlockData.Height;
                            if (poll.PollVotedInFavorBlockData?.Height > maxGoodHeight && this.chainIndexer.GetHeader(poll.PollExecutedBlockData.Hash) != null)
                                maxGoodHeight = poll.PollVotedInFavorBlockData.Height;
                        }

                        if (maxGoodHeight == -1)
                        {
                            this.logger.Info("No common blocks found. Will rebuild the repo from scratch.");
                            this.ResetLocked(transaction);
                            transaction.Commit();
                            return;
                        }

                        this.CurrentTip = new HashHeightPair(this.chainIndexer.GetHeader(maxGoodHeight));

                        this.logger.Info("Common block found at height {0}. Will re-build the repo from there.", this.CurrentTip.Height);

                        // Trim polls to tip.
                        HashSet<Poll> pollsToDelete = new HashSet<Poll>();
                        foreach (Poll poll in polls)
                        {
                            if (poll.PollStartBlockData.Height > this.CurrentTip.Height)
                            {
                                pollsToDelete.Add(poll);
                                continue;
                            }

                            bool modified = false;

                            if (poll.PubKeysHexVotedInFavor.Any(v => v.Height > this.CurrentTip.Height))
                            {
                                poll.PubKeysHexVotedInFavor = poll.PubKeysHexVotedInFavor.Where(v => v.Height <= this.CurrentTip.Height).ToList();
                                modified = true;
                            }

                            if (poll.PollExecutedBlockData?.Height > this.CurrentTip.Height)
                            {
                                poll.PollExecutedBlockData = null;
                                modified = true;
                            }

                            if (poll.PollVotedInFavorBlockData?.Height > this.CurrentTip.Height)
                            {
                                poll.PollVotedInFavorBlockData = null;
                                modified = true;
                            }

                            if (modified)
                                UpdatePoll(transaction, poll);
                        }

                        DeletePollsAndSetHighestPollId(transaction, pollsToDelete.Select(p => p.Id).ToArray());
                        SaveCurrentTip(transaction, this.CurrentTip);
                        transaction.Commit();

                        this.logger.Debug("Polls repo initialized with highest id: {0}.", this.highestPollId);
                    }
                    catch (Exception err) when (err.Message == "No more byte to read")
                    {
                        this.logger.Warn("There was an error reading the polls repository. Will rebuild it.");
                        this.ResetLocked(transaction);
                        transaction.Commit();
                    }
                }
            }
        }

        private void ResetLocked(DBreeze.Transactions.Transaction transaction)
        {
            this.highestPollId = -1;
            transaction.RemoveAllKeys(DataTable, true);
            this.CurrentTip = null;
        }

        public void Reset()
        {
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    ResetLocked(transaction);
                    transaction.Commit();
                }
            }
        }

        public void SaveCurrentTip(DBreeze.Transactions.Transaction transaction, ChainedHeader tip)
        {
            SaveCurrentTip(transaction, (tip == null) ? null : new HashHeightPair(tip));
        }

        public void SaveCurrentTip(DBreeze.Transactions.Transaction transaction, HashHeightPair tip = null)
        {
            lock (this.lockObject)
            {
                if (tip != null)
                    this.CurrentTip = tip;

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
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
                {
                    return func(transaction);
                }
            }
        }

        public void WithTransaction(Action<DBreeze.Transactions.Transaction> action)
        {
            lock (this.lockObject)
            {
                using (DBreeze.Transactions.Transaction transaction = this.dbreeze.GetTransaction())
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
                Dictionary<byte[], byte[]> data = transaction.SelectDictionary<byte[], byte[]>(DataTable);

                return data
                    .Where(d => d.Key.Length == 4)
                    .Select(d => this.dBreezeSerializer.Deserialize<Poll>(d.Value))
                    .ToList();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.dbreeze.Dispose();
        }
    }
}
