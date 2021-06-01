using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Payloads;

namespace Stratis.Features.FederatedPeg.Coordination
{
    public interface ICoordinationManager
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

        /// <summary>Checks if vote was received from the pubkey specified for a particular <see cref="ConversionRequest"/>.</summary>
        bool CheckIfVoted(string requestId, PubKey pubKey);

        /// <summary>Removes all votes associated with provided request Id.</summary>
        void RemoveTransaction(string requestId);

        /// <summary>Provides mapping of all request ids to pubkeys that have voted for them.</summary>
        Dictionary<string, HashSet<PubKey>> GetStatus();

        void AddFeeVote(string requestId, ulong proposedFee, PubKey pubKey);

        ulong GetAgreedTransactionFee(string requestId, int quorum);

        ulong GetCandidateTransactionFee(string requestId);

        void RegisterQuorumSize(int quorum);

        int GetQuorum();

        Task BroadcastAllAsync(Key currentMemberKey);

        Task BroadcastVoteAsync(Key federationKey, string requestId, ulong fee);
    }

    public class CoordinationManager : ICoordinationManager
    {
        private readonly ILogger logger;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;

        // Interflux transaction ID votes
        private Dictionary<string, Dictionary<BigInteger, int>> activeVotes;
        private Dictionary<string, HashSet<PubKey>> receivedVotes;

        // Interflux conversion fee votes
        private Dictionary<string, Dictionary<ulong, int>> activeFeeVotes;
        private Dictionary<string, HashSet<PubKey>> receivedFeeVotes;

        private int quorum;

        private readonly object lockObject = new object();

        public CoordinationManager(
            IFederatedPegBroadcaster federatedPegBroadcaster,
            INodeStats nodeStats)
        {
            this.activeVotes = new Dictionary<string, Dictionary<BigInteger, int>>();
            this.receivedVotes = new Dictionary<string, HashSet<PubKey>>();

            this.activeFeeVotes = new Dictionary<string, Dictionary<ulong, int>>();
            this.receivedFeeVotes = new Dictionary<string, HashSet<PubKey>>();

            // TODO: Need to persist vote storage across node shutdowns

            this.federatedPegBroadcaster = federatedPegBroadcaster;
            this.logger = LogManager.GetCurrentClassLogger();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> Interop Coordination Manager");

            foreach (KeyValuePair<string, Dictionary<ulong, int>> vote in this.activeFeeVotes)
            {
                benchLog.AppendLine("Fee Vote:".PadRight(LoggingConfiguration.ColumnLength) + $" Id: {vote.Key} Votes: {vote.Value.Count}");
            }

            foreach (KeyValuePair<string, Dictionary<BigInteger, int>> vote in this.activeVotes)
            {
                benchLog.AppendLine("Vote:".PadRight(LoggingConfiguration.ColumnLength) + $" Id: {vote.Key} Votes: {vote.Value.Count}");
            }

            benchLog.AppendLine();
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
        public bool CheckIfVoted(string requestId, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                if (!this.receivedVotes.ContainsKey(requestId))
                    return false;

                if (!this.receivedVotes[requestId].Contains(pubKey))
                    return false;

                return true;
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

        public void AddFeeVote(string requestId, ulong proposedFee, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                if (!this.receivedFeeVotes.TryGetValue(requestId, out HashSet<PubKey> voted))
                    voted = new HashSet<PubKey>();

                // Ignore the vote if the pubkey has already submitted a vote.
                if (voted.Contains(pubKey))
                    return;

                this.logger.Info("Pubkey {0} adding vote for request {1}, fee {2}.", pubKey.ToHex(), requestId, proposedFee);

                voted.Add(pubKey);

                if (!this.activeFeeVotes.TryGetValue(requestId, out Dictionary<ulong, int> feeVotes))
                    feeVotes = new Dictionary<ulong, int>();

                if (!feeVotes.ContainsKey(proposedFee))
                    feeVotes[proposedFee] = 1;
                else
                    feeVotes[proposedFee]++;

                this.activeFeeVotes[requestId] = feeVotes;
                this.receivedFeeVotes[requestId] = voted;
            }
        }

        public ulong GetAgreedTransactionFee(string requestId, int quorum)
        {
            lock (this.lockObject)
            {
                if (!this.activeFeeVotes.ContainsKey(requestId))
                    return 0UL;

                ulong highestVoted = 0UL;
                int voteCount = 0;
                foreach (KeyValuePair<ulong, int> vote in this.activeFeeVotes[requestId])
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

        public ulong GetCandidateTransactionFee(string requestId)
        {
            lock (this.lockObject)
            {
                if (!this.activeFeeVotes.ContainsKey(requestId))
                    return 0UL;

                ulong highestVoted = 0UL;
                int voteCount = 0;
                foreach (KeyValuePair<ulong, int> vote in this.activeFeeVotes[requestId])
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

        public void RegisterQuorumSize(int quorum)
        {
            this.quorum = quorum;
        }

        public int GetQuorum()
        {
            return this.quorum;
        }

        public async Task BroadcastAllAsync(Key currentMemberKey)
        {
            foreach (KeyValuePair<string, Dictionary<ulong, int>> request in this.activeFeeVotes)
            {
                string signature = currentMemberKey.SignMessage(request.Key + request.Value.Keys.First());
                await this.federatedPegBroadcaster.BroadcastAsync(new FeeCoordinationPayload(request.Key, request.Value.Keys.First(), signature)).ConfigureAwait(false);
            }
        }

        public async Task BroadcastVoteAsync(Key federationKey, string requestId, ulong fee)
        {
            string signature = federationKey.SignMessage(requestId + fee);

            await this.federatedPegBroadcaster.BroadcastAsync(new FeeCoordinationPayload(requestId, fee, signature)).ConfigureAwait(false);
        }
    }
}
