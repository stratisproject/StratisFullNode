using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.FederatedPeg.Coordination
{
    public interface IConversionRequestCoordinationService
    {
        /// <summary>
        /// Records a vote for a particular transactionId to be associated with the request.
        /// The vote is recorded against the pubkey of the federation member that cast it.
        /// </summary>
        /// <param name="requestId">The identifier of the request.</param>
        /// <param name="transactionId">The voted-for transactionId.</param>
        /// <param name="pubKey">The pubkey of the federation member that signed the incoming message.</param>
        void AddVote(string requestId, BigInteger transactionId, PubKey pubKey);

        /// <summary>
        /// If one of the transaction Ids being voted on has reached a quroum, this will return that transactionId.
        /// </summary>
        /// <param name="requestId">The identifier of the request.</param>
        /// <param name="quorum">The number of votes required for a majority.</param>
        /// <returns>The transactionId of the request that has reached a quorum of votes.</returns>
        BigInteger GetAgreedTransactionId(string requestId, int quorum);

        /// <summary>
        /// Returns the currently highest-voted transactionId.
        /// If there is a tie, one is picked randomly.
        /// </summary>
        /// <param name="requestId">The identifier of the request.</param>
        /// <returns>The transactionId of the highest-voted request.</returns>
        BigInteger GetCandidateTransactionId(string requestId);

        /// <summary>Removes all votes associated with provided request Id.</summary>
        /// <param name="requestId">The identifier of the request.</param>
        void RemoveTransaction(string requestId);

        /// <summary>Provides mapping of all request ids to pubkeys that have voted for them.</summary>
        /// <returns>A dictionary of pubkeys that voted on a request.</returns>
        Dictionary<string, HashSet<PubKey>> GetStatus();

        /// <summary>
        /// Registers the quorum for conversion request transactions, i.e. minimum amount of votes required to process it.
        /// </summary>
        /// <param name="quorum">The amount of votes required.</param>
        void RegisterConversionRequestQuorum(int quorum);

        int GetConversionRequestQuorum();
    }

    public sealed class ConversionRequestCoordinationService : IConversionRequestCoordinationService
    {
        private int conversionRequestQuorum;
        private readonly object lockObject;
        private readonly ILogger logger;

        /// <summary> Interflux transaction ID votes </summary>
        private readonly Dictionary<string, Dictionary<BigInteger, int>> activeVotes;
        private readonly Dictionary<string, HashSet<PubKey>> receivedVotes;

        public ConversionRequestCoordinationService(INodeStats nodeStats)
        {
            this.activeVotes = new Dictionary<string, Dictionary<BigInteger, int>>();
            this.lockObject = new object();
            this.logger = LogManager.GetCurrentClassLogger();
            this.receivedVotes = new Dictionary<string, HashSet<PubKey>>();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
        }

        /// <inheritdoc/>
        public void AddVote(string requestId, BigInteger transactionId, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                if (!this.receivedVotes.TryGetValue(requestId, out HashSet<PubKey> voted))
                    voted = new HashSet<PubKey>();

                // Ignore the vote if the pubkey has already submitted a vote.
                if (voted.Contains(pubKey))
                    return;

                this.logger.Info("Pubkey {0} adding vote for request {1}, transactionId {2}.", pubKey.ToHex(), requestId, transactionId);

                voted.Add(pubKey);

                if (!this.activeVotes.TryGetValue(requestId, out Dictionary<BigInteger, int> transactionIdVotes))
                    transactionIdVotes = new Dictionary<BigInteger, int>();

                if (!transactionIdVotes.ContainsKey(transactionId))
                    transactionIdVotes[transactionId] = 1;
                else
                    transactionIdVotes[transactionId]++;

                this.activeVotes[requestId] = transactionIdVotes;
                this.receivedVotes[requestId] = voted;
            }
        }

        /// <inheritdoc/>
        public BigInteger GetAgreedTransactionId(string requestId, int quorum)
        {
            lock (this.lockObject)
            {
                if (!this.activeVotes.ContainsKey(requestId))
                    return BigInteger.MinusOne;

                BigInteger highestVoted = BigInteger.MinusOne;
                int voteCount = 0;
                foreach (KeyValuePair<BigInteger, int> vote in this.activeVotes[requestId])
                {
                    if (vote.Value > voteCount && vote.Value >= quorum)
                    {
                        highestVoted = vote.Key;
                        voteCount = vote.Value;
                    }
                }

                return highestVoted;
            }
        }

        /// <inheritdoc/>
        public BigInteger GetCandidateTransactionId(string requestId)
        {
            lock (this.lockObject)
            {
                if (!this.activeVotes.ContainsKey(requestId))
                    return BigInteger.MinusOne;

                BigInteger highestVoted = BigInteger.MinusOne;
                int voteCount = 0;
                foreach (KeyValuePair<BigInteger, int> vote in this.activeVotes[requestId])
                {
                    if (vote.Value > voteCount)
                    {
                        highestVoted = vote.Key;
                        voteCount = vote.Value;
                    }
                }

                return highestVoted;
            }
        }

        /// <inheritdoc/>
        public void RemoveTransaction(string requestId)
        {
            lock (this.lockObject)
            {
                this.activeVotes.Remove(requestId);
                this.receivedVotes.Remove(requestId);
            }
        }

        /// <inheritdoc/>
        public Dictionary<string, HashSet<PubKey>> GetStatus()
        {
            lock (this.lockObject)
            {
                return this.receivedVotes;
            }
        }

        /// <inheritdoc/>
        public void RegisterConversionRequestQuorum(int conversionRequestQuorum)
        {
            this.conversionRequestQuorum = conversionRequestQuorum;
        }

        public int GetConversionRequestQuorum()
        {
            return this.conversionRequestQuorum;
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> Interop Conversion Request Votes (last 10):");

            foreach (KeyValuePair<string, Dictionary<BigInteger, int>> active in this.activeVotes.Take(10))
            {
                foreach (KeyValuePair<BigInteger, int> result in active.Value)
                {
                    benchLog.AppendLine($"Active Vote Id: {active.Key} Vote: {result.Key} Count: {result.Value}");
                }
            }

            foreach (KeyValuePair<string, HashSet<PubKey>> received in this.receivedVotes.Take(10))
            {
                benchLog.AppendLine($"Received Vote Id: {received.Key} Votes: {received.Value.Count}");
            }

            benchLog.AppendLine();
        }
    }
}
