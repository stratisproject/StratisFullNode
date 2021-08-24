﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    /// <summary>
    /// Provides fast indexed access to a collection of polls.
    /// </summary>
    public class PollsCollection : IEnumerable<Poll>
    {
        private List<Poll> polls; // Ordered by PollVotedInFavorBlockData?
        private ConcurrentDictionary<VotingData, Poll> pendingPollsByVotingData;

        public PollsCollection(IEnumerable<Poll> polls)
        {
            this.polls = new List<Poll>();
            this.pendingPollsByVotingData = new ConcurrentDictionary<VotingData, Poll>();
            foreach (Poll poll in polls)
                this.Add(poll);
        }

        public static implicit operator PollsCollection(List<Poll> polls)
        {
            return new PollsCollection(polls);
        }

        public IEnumerator<Poll> GetEnumerator()
        {
            return ((IEnumerable<Poll>)this.polls).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        public void Add(Poll poll)
        {
            if (poll.IsPending)
            {
                // Can't insert another pending poll for the same.
                Guard.Assert(!this.pendingPollsByVotingData.ContainsKey(poll.VotingData));
                this.pendingPollsByVotingData[poll.VotingData] = poll;
            }

            this.polls.Add(poll);
        }

        public bool Remove(Poll poll)
        {
            if (this.polls.Remove(poll))
            {
                if (poll.IsPending)
                    this.pendingPollsByVotingData.Remove(poll.VotingData, out _);

                return true;
            }

            return false;
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

        /// <summary>
        /// Call this when the poll's pending status changes.
        /// </summary>
        /// <param name="poll">The poll that changed.</param>
        public void OnPendingStatusChanged(Poll poll)
        {
            if (poll.IsPending)
            {
                // Can't insert another pending poll for the same.
                Guard.Assert(!this.pendingPollsByVotingData.ContainsKey(poll.VotingData));
                this.pendingPollsByVotingData[poll.VotingData] = poll;
            }
            else
            {
                this.pendingPollsByVotingData.Remove(poll.VotingData, out _);
            }
        }
    }
}