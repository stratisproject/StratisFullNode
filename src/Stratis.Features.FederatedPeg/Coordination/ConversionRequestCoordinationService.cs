using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Conversion;

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

        /// <summary>Provides mapping of all current transaction ids for requests, mapped to pubkeys that have voted for them.</summary>
        /// <remarks>May not contain every request, depending on whether the node has been rebooted recently.</remarks>
        /// <returns>A dictionary of pubkeys that voted on each request.</returns>
        Dictionary<string, HashSet<PubKey>> GetStatus();

        /// <summary>Provides mapping of all request ids to the vote tally per transactionId for that request.</summary>
        /// <returns>A dictionary of vote tallies per potential transactionId for a given request.</returns>
        Dictionary<string, Dictionary<BigInteger, int>> GetTransactionIdStatus();

        /// <summary>
        /// Registers the quorum for conversion request transactions, i.e. minimum amount of votes required to process it.
        /// </summary>
        /// <param name="quorum">The amount of votes required.</param>
        void RegisterConversionRequestQuorum(int quorum);

        int GetConversionRequestQuorum();
    }

    public sealed class ConversionRequestCoordinationService : IConversionRequestCoordinationService
    {
        private readonly IConversionRequestCoordinationVoteRepository voteRepository;

        private int conversionRequestQuorum;
        private readonly object lockObject;
        private readonly ILogger logger;

        private List<string> activeRequests;

        public ConversionRequestCoordinationService(INodeStats nodeStats, IConversionRequestCoordinationVoteRepository voteRepository)
        {
            this.voteRepository = voteRepository;
            this.lockObject = new object();
            this.logger = LogManager.GetCurrentClassLogger();

            this.activeRequests = new List<string>();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name, 252);
        }

        /// <inheritdoc/>
        public void AddVote(string requestId, BigInteger transactionId, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                List<ConversionRequestCoordinationVote> votes = this.voteRepository.GetAll(requestId);

                var voted = new HashSet<PubKey>();

                foreach (ConversionRequestCoordinationVote vote in votes)
                {
                    voted.Add(vote.PubKey);
                }
                
                // Check if the pubkey has voted for this request.
                if (!voted.Contains(pubKey))
                {
                    this.logger.LogDebug("Adding vote for request '{0}' (transactionId '{1}') from pubkey {2}.", requestId, transactionId, pubKey.ToHex());

                    this.voteRepository.Save(new ConversionRequestCoordinationVote
                    {
                        RequestId = requestId,
                        TransactionId = transactionId,
                        PubKey = pubKey
                    });

                    this.activeRequests.Add(requestId);

                    if (this.activeRequests.Count > 5)
                    {
                        this.activeRequests = this.activeRequests.TakeLast(5).ToList();
                    }
                }
            }
        }

        /// <inheritdoc/>
        public BigInteger GetAgreedTransactionId(string requestId, int quorum)
        {
            lock (this.lockObject)
            {
                Dictionary<BigInteger, int> transactionIdVotes = GetTransactionIdVotes(requestId);
                
                BigInteger highestVoted = BigInteger.MinusOne;
                int voteCount = 0;
                foreach (KeyValuePair<BigInteger, int> vote in transactionIdVotes)
                {
                    if (vote.Value <= voteCount || vote.Value < quorum)
                        continue;

                    highestVoted = vote.Key;
                    voteCount = vote.Value;
                }

                return highestVoted;
            }
        }

        /// <inheritdoc/>
        public BigInteger GetCandidateTransactionId(string requestId)
        {
            lock (this.lockObject)
            {
                Dictionary<BigInteger, int> transactionIdVotes = GetTransactionIdVotes(requestId);

                BigInteger highestVoted = BigInteger.MinusOne;
                int voteCount = 0;
                foreach (KeyValuePair<BigInteger, int> vote in transactionIdVotes)
                {
                    if (vote.Value <= voteCount) 
                        continue;

                    highestVoted = vote.Key;
                    voteCount = vote.Value;
                }

                return highestVoted;
            }
        }

        /// <inheritdoc/>
        public void RemoveTransaction(string requestId)
        {
            // TODO: Now that we persist the votes to disk it is unclear how long they should be retained for nodes that do not yet have knowledge of what was voted. Possibly when the conversion request is marked as processed?
        }

        /// <inheritdoc/>
        public Dictionary<string, HashSet<PubKey>> GetStatus()
        {
            lock (this.lockObject)
            {
                List<ConversionRequestCoordinationVote> allVotes = this.voteRepository.GetAll();

                var receivedVotes = new Dictionary<string, HashSet<PubKey>>();

                foreach (var vote in allVotes)
                {
                    if (!receivedVotes.TryGetValue(vote.RequestId, out HashSet<PubKey> whoVoted))
                    {
                        whoVoted = new HashSet<PubKey>();
                    }

                    whoVoted.Add(vote.PubKey);
                    receivedVotes[vote.RequestId] = whoVoted;
                }

                return receivedVotes;
            }
        }

        /// <inheritdoc/>
        public Dictionary<string, Dictionary<BigInteger, int>> GetTransactionIdStatus()
        {
            lock (this.lockObject)
            {
                return this.transactionIdVotes;
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

        private Dictionary<BigInteger, int> GetTransactionIdVotes(string requestId)
        {
            List<ConversionRequestCoordinationVote> voted = this.voteRepository.GetAll(requestId);

            var transactionIdVotes = new Dictionary<BigInteger, int>();

            foreach (ConversionRequestCoordinationVote vote in voted)
            {
                if (!transactionIdVotes.TryGetValue(vote.TransactionId, out int tempCount))
                    tempCount = 0;

                transactionIdVotes[vote.TransactionId] = ++tempCount;
            }

            return transactionIdVotes;
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> InterFlux Conversion Request Votes (last 5):");

            foreach (string active in this.activeRequests)
            {
                foreach (KeyValuePair<BigInteger, int> result in GetTransactionIdVotes(active))
                {
                    benchLog.AppendLine($"Request Id: {active} Transaction Id: {result.Key} Count: {result.Value}");
                }
            }

            benchLog.AppendLine();
        }
    }
}
