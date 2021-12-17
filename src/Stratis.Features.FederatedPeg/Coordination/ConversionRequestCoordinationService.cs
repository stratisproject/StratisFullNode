using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
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
        private readonly Dictionary<string, Dictionary<BigInteger, int>> transactionIdVotes;
        private readonly Dictionary<string, HashSet<PubKey>> receivedVotes;

        public ConversionRequestCoordinationService(INodeStats nodeStats)
        {
            this.lockObject = new object();
            this.logger = LogManager.GetCurrentClassLogger();

            this.receivedVotes = new Dictionary<string, HashSet<PubKey>>();
            this.transactionIdVotes = new Dictionary<string, Dictionary<BigInteger, int>>();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name, 252);
        }

        /// <inheritdoc/>
        public void AddVote(string requestId, BigInteger transactionId, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                // If the request has not yet been voted on, create a voting list.
                if (!this.receivedVotes.TryGetValue(requestId, out HashSet<PubKey> voted))
                    voted = new HashSet<PubKey>();

                // Check if the pubkey node has voted for this request.
                if (!voted.Contains(pubKey))
                {
                    this.logger.Debug("Adding vote for request '{0}' (transactionId '{1}') from pubkey {2}.", requestId, transactionId, pubKey.ToHex());

                    voted.Add(pubKey);

                    // If the set of active votes does not contain the request, create a new list.
                    if (!this.transactionIdVotes.TryGetValue(requestId, out Dictionary<BigInteger, int> transactionIdVotesForRequestId))
                        transactionIdVotesForRequestId = new Dictionary<BigInteger, int>();

                    if (!transactionIdVotesForRequestId.ContainsKey(transactionId))
                        transactionIdVotesForRequestId[transactionId] = 1;
                    else
                        transactionIdVotesForRequestId[transactionId]++;

                    this.transactionIdVotes[requestId] = transactionIdVotesForRequestId;
                    this.receivedVotes[requestId] = voted;
                }
            }
        }

        /// <inheritdoc/>
        public BigInteger GetAgreedTransactionId(string requestId, int quorum)
        {
            lock (this.lockObject)
            {
                if (!this.transactionIdVotes.ContainsKey(requestId))
                    return BigInteger.MinusOne;

                BigInteger highestVoted = BigInteger.MinusOne;
                int voteCount = 0;
                foreach (KeyValuePair<BigInteger, int> vote in this.transactionIdVotes[requestId])
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
                if (!this.transactionIdVotes.ContainsKey(requestId))
                    return BigInteger.MinusOne;

                BigInteger highestVoted = BigInteger.MinusOne;
                int voteCount = 0;
                foreach (KeyValuePair<BigInteger, int> vote in this.transactionIdVotes[requestId])
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
                this.transactionIdVotes.Remove(requestId);
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
            benchLog.AppendLine(">> InterFlux Conversion Request Votes (last 5):");

            foreach (KeyValuePair<string, Dictionary<BigInteger, int>> active in this.transactionIdVotes.Take(5))
            {
                foreach (KeyValuePair<BigInteger, int> result in active.Value)
                {
                    benchLog.AppendLine($"Request Id: {active.Key} Transaction Id: {result.Key} Count: {result.Value}");
                }
            }

            benchLog.AppendLine();
        }
    }
}
