﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Utils;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
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

        private readonly PoANetwork network;

        public class Transaction : IDisposable
        {
            private readonly PollsRepository pollsRepository;
            public bool IsModified { get; private set; }

            private DBreeze.Transactions.Transaction _transaction;
            private DBreeze.Transactions.Transaction transaction 
            { 
                get 
                {
                    if (this._transaction == null)
                        this._transaction = this.pollsRepository.dbreeze.GetTransaction();
                    
                    return this._transaction;  
                } 

                set 
                { 
                    this._transaction = value; 
                } 
            }

            public Transaction(PollsRepository pollsRepository)
            {
                this.pollsRepository = pollsRepository;
                this.IsModified = false;
            }

            public void Insert<TKey, TValue>(string tableName, TKey key, TValue value)
            {
                this.transaction.Insert(tableName, key, value);
                this.IsModified = true;
            }

            public Row<TKey, TValue> Select<TKey, TValue>(string tableName, TKey key)
            {
                return this.transaction.Select<TKey, TValue>(tableName, key);
            }

            public Dictionary<TKey, TValue> SelectDictionary<TKey, TValue>(string tableName)
            {
                return this.transaction.SelectDictionary<TKey, TValue>(tableName);
            }

            public void RemoveKey<TKey>(string tableName, TKey key)
            {
                this.transaction.RemoveKey(tableName, key);
                this.IsModified = true;
            }

            public void RemoveAllKeys(string tableName, bool withFileRecreation)
            {
                this.transaction.RemoveAllKeys(tableName, withFileRecreation);
                this.IsModified = true;
            }

            public void Flush()
            {
                this.Commit();

                this.transaction.Dispose();
                this.transaction = null;
            }

            public void SetTip(ChainedHeader tip)
            {
                this.pollsRepository.CurrentTip = new HashHeightPair(tip);
                this.IsModified = true;
            }

            public void Commit()
            {
                if (this.IsModified)
                {
                    this.SaveCurrentTip();
                    this.transaction.Commit();
                    this.IsModified = false;
                }
            }

            public void Dispose()
            {
                this.transaction?.Dispose();
            }

            /// <summary>Adds new polls.</summary>
            /// <param name="polls">The polls to add.</param>
            public void AddPolls(params Poll[] polls)
            {
                foreach (Poll pollToAdd in polls.OrderBy(p => p.Id))
                {
                    if (pollToAdd.Id != this.pollsRepository.highestPollId + 1)
                        throw new ArgumentException("Id is incorrect. Gaps are not allowed.");

                    byte[] bytes = this.pollsRepository.dBreezeSerializer.Serialize(pollToAdd);

                    this.Insert(DataTable, pollToAdd.Id.ToBytes(), bytes);

                    this.pollsRepository.highestPollId++;
                }
            }

            /// <summary>Updates existing poll.</summary>
            /// <param name="poll">The poll to update.</param>
            public void UpdatePoll(Poll poll)
            {
                byte[] bytes = this.pollsRepository.dBreezeSerializer.Serialize(poll);

                this.Insert(DataTable, poll.Id.ToBytes(), bytes);

            }

            /// <summary>Loads polls under provided keys from the database.</summary>
            /// <param name="ids">The ids of the polls to retrieve.</param>
            /// <returns>A list of retrieved <see cref="Poll"/> entries.</returns>
            public List<Poll> GetPolls(params int[] ids)
            {
                var polls = new List<Poll>(ids.Length);

                foreach (int id in ids)
                {
                    Row<byte[], byte[]> row = this.Select<byte[], byte[]>(DataTable, id.ToBytes());

                    if (!row.Exists)
                        throw new ArgumentException("Value under provided key doesn't exist!");

                    Poll poll = this.pollsRepository.dBreezeSerializer.Deserialize<Poll>(row.Value);

                    polls.Add(poll);
                }

                return polls;
            }

            /// <summary>Loads all polls from the database.</summary>
            /// <returns>A list of retrieved <see cref="Poll"/> entries.</returns>
            public List<Poll> GetAllPolls()
            {
                Dictionary<byte[], byte[]> data = this.SelectDictionary<byte[], byte[]>(DataTable);

                return data
                    .Where(d => d.Key.Length == 4)
                    .Select(d => this.pollsRepository.dBreezeSerializer.Deserialize<Poll>(d.Value))
                    .ToList();
            }

            private void SaveCurrentTip()
            {
                if (this.pollsRepository.CurrentTip != null)
                {
                    this.Insert(DataTable, RepositoryTipKey, this.pollsRepository.dBreezeSerializer.Serialize(this.pollsRepository.CurrentTip));
                }
            }

            /// <summary>Removes polls for the provided ids.</summary>
            /// <param name="ids">The ids of the polls to remove.</param>
            public void DeletePollsAndSetHighestPollId(params int[] ids)
            {
                foreach (int pollId in ids.OrderBy(a => a))
                {
                    this.RemoveKey(DataTable, pollId.ToBytes());
                }

                List<Poll> polls = this.GetAllPolls();
                this.pollsRepository.highestPollId = (polls.Count == 0) ? -1 : polls.Max(a => a.Id);
            }

            /// <summary>Removes polls under provided ids.</summary>
            /// <param name="ids">The ids of the polls to remove.</param>
            public void RemovePolls(params int[] ids)
            {
                foreach (int pollId in ids.OrderBy(id => id).Reverse())
                {
                    if (this.pollsRepository.highestPollId != pollId)
                        throw new ArgumentException("Only deletion of the most recent item is allowed!");

                    this.RemoveKey<byte[]>(DataTable, pollId.ToBytes());

                    this.pollsRepository.highestPollId--;
                }
            }

            public void ResetLocked()
            {
                this.pollsRepository.highestPollId = -1;
                this.RemoveAllKeys(DataTable, true);
                this.pollsRepository.CurrentTip = null;
            }
        }

        public PollsRepository(ChainIndexer chainIndexer, DataFolder dataFolder, DBreezeSerializer dBreezeSerializer, PoANetwork network)
        {
            Guard.NotEmpty(dataFolder.PollsPath, nameof(dataFolder.PollsPath));

            Directory.CreateDirectory(dataFolder.PollsPath);
            this.chainIndexer = chainIndexer;
            this.dbreeze = new DBreezeEngine(dataFolder.PollsPath);
            this.dBreezeSerializer = dBreezeSerializer;
            this.network = network;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        public void Initialize()
        {
            // Load highest index.
            lock (this.lockObject)
            {
                using (var transaction = new Transaction(this))
                {
                    try
                    {
                        List<Poll> polls = transaction.GetAllPolls();

                        // If the polls repository contains duplicate polls then reset the highest poll id and 
                        // set the tip to null.
                        // This will trigger the VotingManager to rebuild the voting and polls repository as the
                        // polls repository tip is null. This happens later during startup, see VotingManager.Synchronize()
                        var uniquePolls = new HashSet<Poll>(polls);
                        if (uniquePolls.Count != polls.Count)
                        {
                            this.logger.LogWarning("The polls repository contains {0} duplicate polls, it will be rebuilt.", polls.Count - uniquePolls.Count);

                            transaction.ResetLocked();
                            transaction.Commit();
                            return;
                        }

                        // Check to see if a polls repository tip is saved, if not rebuild the repo.
                        Row<byte[], byte[]> rowTip = transaction.Select<byte[], byte[]>(DataTable, RepositoryTipKey);
                        if (!rowTip.Exists)
                        {
                            this.logger.LogInformation("The polls repository tip is unknown, it will be rebuilt.");

                            transaction.ResetLocked();
                            transaction.Commit();
                            return;
                        }

                        // Check to see if the polls repo tip exists in chain.
                        // The node could have been rewound so we need to rebuild the repo from that point.
                        this.CurrentTip = this.dBreezeSerializer.Deserialize<HashHeightPair>(rowTip.Value);
                        ChainedHeader chainedHeaderTip = this.chainIndexer.GetHeader(this.CurrentTip.Hash);
                        if (chainedHeaderTip != null)
                        {
                            this.highestPollId = (polls.Count > 0) ? polls.Max(p => p.Id) : -1;
                            this.logger.LogInformation("Polls repository tip exists on chain; initializing at height {0}; highest poll id: {1}.", this.CurrentTip.Height, this.highestPollId);
                            return;
                        }

                        this.logger.LogInformation("The polls repository tip {0} was not found in the consensus chain, determining fork.", this.CurrentTip);

                        // The polls repository tip could not be found in the chain.
                        // Look at all other known hash/height pairs to find something in common with the consensus chain.
                        // We will take that as the last known valid height.
                        int maxGoodHeight = -1;
                        foreach (Poll poll in polls)
                        {
                            if (poll.PollStartBlockData.Height > maxGoodHeight && this.chainIndexer.GetHeader(poll.PollStartBlockData.Hash) != null)
                                maxGoodHeight = poll.PollStartBlockData.Height;
                            if (poll.PollExecutedBlockData?.Height > maxGoodHeight && this.chainIndexer.GetHeader(poll.PollExecutedBlockData.Hash) != null)
                                maxGoodHeight = poll.PollExecutedBlockData.Height;
                            if (poll.PollVotedInFavorBlockData?.Height > maxGoodHeight && this.chainIndexer.GetHeader(poll.PollVotedInFavorBlockData.Hash) != null)
                                maxGoodHeight = poll.PollVotedInFavorBlockData.Height;
                        }

                        if (maxGoodHeight == -1)
                        {
                            this.logger.LogInformation("No common blocks found; the repo will be rebuilt from scratch.");

                            transaction.ResetLocked();
                            transaction.Commit();
                            return;
                        }

                        this.CurrentTip = new HashHeightPair(this.chainIndexer.GetHeader(maxGoodHeight));

                        this.logger.LogInformation("Common block found at height {0}; the repo will be rebuilt from there.", this.CurrentTip.Height);

                        // Trim polls to tip.
                        HashSet<Poll> pollsToDelete = new HashSet<Poll>();
                        foreach (Poll poll in polls)
                        {
                            if (poll.PollStartBlockData.Height > this.CurrentTip.Height)
                            {
                                pollsToDelete.Add(poll);
                                this.logger.LogDebug("Poll {0} will be deleted.", poll.Id);
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

                            // Check if the current tip is before the poll expiry activation block,
                            // if so un-expire it.
                            if (poll.IsExpired && !IsPollExpiredAt(poll, this.CurrentTip.Height, this.network))
                            {
                                this.logger.LogDebug("Un-expiring poll {0}.", poll.Id);
                                poll.IsExpired = false;

                                modified = true;
                            }

                            if (modified)
                                transaction.UpdatePoll(poll);
                        }

                        transaction.DeletePollsAndSetHighestPollId(pollsToDelete.Select(p => p.Id).ToArray());
                        transaction.Commit();

                        this.logger.LogInformation("Polls repository initialized at height {0}; highest poll id: {1}.", this.CurrentTip.Height, this.highestPollId);
                    }
                    catch (Exception err) when (err.Message == "No more byte to read")
                    {
                        this.logger.LogWarning("There was an error reading the polls repository, it will be rebuild.");

                        transaction.ResetLocked();
                        transaction.Commit();
                    }
                }
            }
        }

        public void Reset()
        {
            lock (this.lockObject)
            {
                using (var transaction = new Transaction(this))
                {
                    transaction.ResetLocked();
                    transaction.Commit();
                }
            }
        }

        /// <summary>Provides Id of the most recently added poll.</summary>
        /// <returns>Id of the most recently added poll.</returns>
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

        public T WithTransaction<T>(Func<Transaction, T> func)
        {
            lock (this.lockObject)
            {
                using (var transaction = new Transaction(this))
                {
                    return func(transaction);
                }
            }
        }

        public void WithTransaction(Action<Transaction> action)
        {
            lock (this.lockObject)
            {
                using (var transaction = new Transaction(this))
                {
                    action(transaction);
                }
            }
        }

        private static int GetPollExpiryHeight(Poll poll, PoANetwork network)
        {
            return Math.Max(poll.PollStartBlockData.Height + network.ConsensusOptions.PollExpiryBlocks, network.ConsensusOptions.Release1100ActivationHeight);
        }

        public static int GetPollExpiryOrExecutionHeight(Poll poll, PoANetwork network)
        {
            if (poll.IsApproved)
                return poll.PollVotedInFavorBlockData.Height + (int)network.Consensus.MaxReorgLength;

            return GetPollExpiryHeight(poll, network);
        }

        public static bool IsPollExpiredAt(Poll poll, int height, PoANetwork network)
        {
            return GetPollExpiryHeight(poll, network) <= height;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.dbreeze.Dispose();
        }
    }
}
