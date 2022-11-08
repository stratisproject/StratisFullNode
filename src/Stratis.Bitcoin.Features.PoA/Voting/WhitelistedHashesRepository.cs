using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public class WhitelistedHashesRepository : IWhitelistedHashesRepository
    {
        /// <summary>Protects access to <see cref="whitelistedHashes"/>.</summary>
        private readonly object locker;

        private readonly ILogger logger;

        private readonly PoAConsensusOptions poaConsensusOptions;

        // Dictionary of hash histories. Even list entries are additions and odd entries are removals.
        private Dictionary<uint256, int[]> whitelistedHashes;

        public WhitelistedHashesRepository(ILoggerFactory loggerFactory, Network network)
        {
            this.locker = new object();

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.poaConsensusOptions = network.Consensus.Options as PoAConsensusOptions;
        }

        public class PollComparer : IComparer<(int height, int id)>
        {
            public int Compare((int height, int id) poll1, (int height, int id) poll2)
            {
                int cmp = poll1.height.CompareTo(poll2.height);
                if (cmp != 0)
                    return cmp;

                return poll1.id.CompareTo(poll2.id);
            }
        }

        static PollComparer pollComparer = new PollComparer();

        private void GetWhitelistedHashesFromExecutedPolls(VotingManager votingManager)
        {
            lock (this.locker)
            {
                var federation = new List<IFederationMember>(this.poaConsensusOptions.GenesisFederationMembers);

                IEnumerable<Poll> executedPolls = votingManager.GetExecutedPolls().WhitelistPolls();
                foreach (Poll poll in executedPolls.OrderBy(a => (a.PollExecutedBlockData.Height, a.Id), pollComparer))
                {
                    var hash = new uint256(poll.VotingData.Data);

                    if (poll.VotingData.Key == VoteKey.WhitelistHash)
                    {
                        this.AddHash(hash, poll.PollExecutedBlockData.Height);
                    }
                    else if (poll.VotingData.Key == VoteKey.RemoveHash)
                    {
                        this.RemoveHash(hash, poll.PollExecutedBlockData.Height);
                    }
                }
            }
        }

        public void Initialize(VotingManager votingManager)
        {
            // TODO: Must call Initialize before the Mempool rules try to use this class.
            lock (this.locker)
            {
                this.whitelistedHashes = new Dictionary<uint256, int[]>();
                this.GetWhitelistedHashesFromExecutedPolls(votingManager);
            }
        }

        public void AddHash(uint256 hash, int executionHeight)
        {
            lock (this.locker)
            {
                // Retrieve the whitelist history for this hash.
                if (!this.whitelistedHashes.TryGetValue(hash, out int[] history))
                {
                    this.whitelistedHashes[hash] = new int[] { executionHeight };
                    return;
                }

                // Keep all history up to and including the executionHeight.
                int keep = BinarySearch.BinaryFindFirst((k) => k == history.Length || history[k] > executionHeight, 0, history.Length + 1);
                Array.Resize(ref history, keep | 1);
                this.whitelistedHashes[hash] = history;

                // If the history is an even length then add the addition height to signify addition.
                if ((keep % 2) == 0)
                {
                    // Add an even indexed entry to signify an addition.
                    history[keep] = executionHeight;
                    return;
                }

                this.logger.LogTrace("(-)[HASH_ALREADY_EXISTS]");
                return;
            }
        }

        public void RemoveHash(uint256 hash, int executionHeight)
        {
            lock (this.locker)
            {
                // Retrieve the whitelist history for this hash.
                if (this.whitelistedHashes.TryGetValue(hash, out int[] history))
                {
                    // Keep all history up to and including the executionHeight.
                    int keep = BinarySearch.BinaryFindFirst((k) => k == history.Length || history[k] >= executionHeight, 0, history.Length + 1);
                    Array.Resize(ref history, (keep + 1) & ~1);
                    this.whitelistedHashes[hash] = history;

                    // If the history is an odd length then add the removal height to signify removal.
                    if ((keep % 2) != 0)
                    {
                        history[keep] = executionHeight;
                        return;
                    }
                }

                this.logger.LogTrace("(-)[HASH_DOESNT_EXIST]");
                return;
            }
        }

        private bool ExistsHash(uint256 hash, int blockHeight)
        {
            lock (this.locker)
            {
                // Retrieve the whitelist history for this hash.
                if (!this.whitelistedHashes.TryGetValue(hash, out int[] history))
                    return false;

                int keep = BinarySearch.BinaryFindFirst((k) => k == history.Length || history[k] > blockHeight, 0, history.Length + 1);
                return (keep % 2) != 0;
            }
        }

        public List<uint256> GetHashes(int blockHeight = int.MaxValue)
        {
            lock (this.locker)
            {
                return this.whitelistedHashes.Where(k => ExistsHash(k.Key, blockHeight)).Select(k => k.Key).ToList();
            }
        }
    }

    public interface IWhitelistedHashesRepository
    {
        void AddHash(uint256 hash, int executionHeight);

        void RemoveHash(uint256 hash, int executionHeight);

        List<uint256> GetHashes(int blockHeight = int.MaxValue);

        void Initialize(VotingManager votingManager);
    }
}