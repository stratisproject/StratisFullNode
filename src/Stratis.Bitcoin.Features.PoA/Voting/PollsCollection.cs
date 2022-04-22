using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonConverters;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    /// <summary>
    /// Provides fast indexed access to a collection of polls.
    /// </summary>
    public class PollsCollection : IEnumerable<Poll>
    {
        private readonly PoANetwork network;
        private readonly ILogger logger;
        private readonly HashSet<Poll> polls;
        private readonly ConcurrentDictionary<VotingData, Poll> pendingPollsByVotingData;
        private readonly Dictionary<int, List<Poll>> pollsByExpiryOrExecutionHeight;

        public PollsCollection(PoANetwork network, IEnumerable<Poll> polls)
        {
            this.network = network;
            this.polls = new HashSet<Poll>();
            this.pendingPollsByVotingData = new ConcurrentDictionary<VotingData, Poll>();
            this.pollsByExpiryOrExecutionHeight = new Dictionary<int, List<Poll>>();

            this.logger = LogManager.GetCurrentClassLogger();

            foreach (Poll poll in polls)
                this.Add(poll);
        }

        public void LogPolls(ChainedHeader tip, List<IFederationMember> federationMembers, IPollResultExecutor pollResultExecutor)
        {
            var settings = new JsonSerializerSettings();
            settings.Converters.Add(new UInt256JsonConverter());
            string pollLog = $"Polls: {Environment.NewLine}{string.Join(Environment.NewLine, this.polls.Select(p => JsonConvert.SerializeObject(new PollViewModel(p, pollResultExecutor), settings)))}{Environment.NewLine}Members: {Environment.NewLine}{string.Join(Environment.NewLine, federationMembers)}";

            var hashCode = Utils.GetHashCode(Encoding.ASCII.GetBytes(pollLog));

            this.logger.LogDebug("Height {0}: HashCode({1}) {2}.", tip.Height, hashCode, pollLog);
        }

        public IEnumerator<Poll> GetEnumerator()
        {
            return ((IEnumerable<Poll>)this.polls).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        private void IndexPoll(Poll poll)
        {
            if (poll.IsPending || (poll.IsApproved && !poll.IsExecuted))
            {
                if (poll.IsPending)
                {
                    // Can't insert another pending poll for the same.
                    if (!this.pendingPollsByVotingData.ContainsKey(poll.VotingData))
                        this.pendingPollsByVotingData[poll.VotingData] = poll;
                }

                int expiryOrExecutionHeight = PollsRepository.GetPollExpiryOrExecutionHeight(poll, this.network);
                if (!this.pollsByExpiryOrExecutionHeight.TryGetValue(expiryOrExecutionHeight, out List<Poll> polls))
                {
                    polls = new List<Poll>();
                    this.pollsByExpiryOrExecutionHeight[expiryOrExecutionHeight] = polls;
                }

                polls.Add(poll);
            }
        }

        public void Add(Poll poll)
        {
            if (this.polls.Contains(poll))
            {
                this.logger.LogWarning("The poll already exists: '{0}'.", poll);
                return;
            }

            this.polls.Add(poll);

            this.IndexPoll(poll);
        }

        private void UnIndexPoll(Poll poll)
        {
            if (poll.IsPending || (poll.IsApproved && !poll.IsExecuted))
            {
                if (poll.IsPending)
                    this.pendingPollsByVotingData.Remove(poll.VotingData, out _);

                int expiryOrExecutionHeight = PollsRepository.GetPollExpiryOrExecutionHeight(poll, this.network);
                if (this.pollsByExpiryOrExecutionHeight.TryGetValue(expiryOrExecutionHeight, out List<Poll> polls))
                {                    
                    polls.Remove(poll);
                    if (polls.Count == 0)
                        this.pollsByExpiryOrExecutionHeight.Remove(expiryOrExecutionHeight);
                }
            }
        }

        public bool Remove(Poll poll)
        {
            if (this.polls.Remove(poll))
            {
                UnIndexPoll(poll);

                return true;
            }

            return false;
        }

        public List<Poll> GetPollsToExecuteOrExpire(int height)
        {
            if (this.pollsByExpiryOrExecutionHeight.TryGetValue(height, out List<Poll> polls))
                return new List<Poll>(polls);
            
            return new List<Poll>();
        }

        public void AdjustPoll(Poll poll, Action<Poll> action)
        {
            UnIndexPoll(poll);
            action(poll);
            IndexPoll(poll);
        }

        public Poll GetPendingPollByVotingData(VotingData votingData)
        {
            if (this.pendingPollsByVotingData.TryGetValue(votingData, out Poll poll))
            {
                // The poll should no longer be in this collection if its not pending.
                Guard.Assert(poll.IsPending);
                return poll;
            }

            return null;
        }
    }
}
