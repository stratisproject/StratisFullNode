using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.PoA;
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

        bool HasProposalConcluded(string requestId);

        void ProposeFeeFromMultiSigMember(string requestId, ulong feeAmount, PubKey pubKey);
    }

    public sealed class CoordinationManager : ICoordinationManager
    {
        private readonly IExternalApiPoller externalApiPoller;
        private readonly IFederationManager federationManager;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly ILogger logger;

        // Interflux transaction ID votes
        private Dictionary<string, Dictionary<BigInteger, int>> activeVotes;
        private Dictionary<string, HashSet<PubKey>> receivedVotes;

        // Interflux conversion fee votes
        private Dictionary<string, Dictionary<ulong, int>> activeFeeVotes;
        private Dictionary<string, HashSet<PubKey>> receivedFeeVotes;

        // Proposed fees by request id
        private Dictionary<string, Dictionary<PubKey, ulong>> feeProposalsByRequestId;
        private int quorum;

        private readonly object lockObject = new object();

        public CoordinationManager(
            IExternalApiPoller externalApiPoller,
            IFederationManager federationManager,
            IFederatedPegBroadcaster federatedPegBroadcaster,
            INodeStats nodeStats)
        {
            this.activeVotes = new Dictionary<string, Dictionary<BigInteger, int>>();
            this.receivedVotes = new Dictionary<string, HashSet<PubKey>>();

            this.activeFeeVotes = new Dictionary<string, Dictionary<ulong, int>>();
            this.receivedFeeVotes = new Dictionary<string, HashSet<PubKey>>();

            this.feeProposalsByRequestId = new Dictionary<string, Dictionary<PubKey, ulong>>();

            // TODO: Need to persist vote storage across node shutdowns

            this.externalApiPoller = externalApiPoller;
            this.federationManager = federationManager;
            this.federatedPegBroadcaster = federatedPegBroadcaster;
            this.logger = LogManager.GetCurrentClassLogger();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> Interop Coordination Manager");

            foreach (KeyValuePair<string, Dictionary<PubKey, ulong>> vote in this.feeProposalsByRequestId)
            {
                IEnumerable<long> values = vote.Value.Values.Select(s => Convert.ToInt64(s));

                var state = vote.Value.Count >= this.quorum ? "Concluded" : "In Progress";
                benchLog.AppendLine($"Fee Proposal Id: {vote.Key} Proposals: {vote.Value.Count} Fee (Avg): {values.Average()} State: {state}");
            }

            benchLog.AppendLine();
        }

        /// <inheritdoc/>
        public bool HasProposalConcluded(string requestId)
        {
            lock (this.lockObject)
            {
                bool isProposalConcluded = false;

                // If the request id doesn't exist yet propose the fee and broadcast it.
                if (!this.feeProposalsByRequestId.TryGetValue(requestId, out Dictionary<PubKey, ulong> proposals))
                {
                    ulong candidateFee = (ulong)(this.externalApiPoller.EstimateConversionTransactionFee() * 100_000_000m);

                    this.logger.Debug($"No nodes has proposed a fee of {candidateFee} for conversion request id '{requestId}'.");

                    this.feeProposalsByRequestId.Add(requestId, new Dictionary<PubKey, ulong>() { { this.federationManager.CurrentFederationKey.PubKey, candidateFee } });
                }
                else
                {
                    if (!proposals.Any(p => p.Key == this.federationManager.CurrentFederationKey.PubKey))
                    {
                        ulong candidateFee = (ulong)(this.externalApiPoller.EstimateConversionTransactionFee() * 100_000_000m);

                        this.logger.Debug($"Adding proposed fee of {candidateFee} for conversion request id '{requestId}'.");
                        proposals.Add(this.federationManager.CurrentFederationKey.PubKey, candidateFee);
                    }
                    else
                        this.logger.Debug($"This node has already proposed a fee for conversion request id '{requestId}'.");
                }

                this.feeProposalsByRequestId.TryGetValue(requestId, out proposals);

                this.logger.Debug($"{proposals.Count} node(s) has proposed a fee for conversion request id {requestId}.");

                if (proposals.Count >= this.quorum)
                {
                    IEnumerable<long> values = proposals.Values.Select(s => Convert.ToInt64(s));
                    this.logger.Debug($"Proposal fee for request id '{requestId}' has concluded; average amount: {values.Average()}");
                    isProposalConcluded = true;
                }
                else
                {
                    // If the proposal is not concluded, broadcast again.
                    KeyValuePair<PubKey, ulong> myProposal = proposals.First(p => p.Key == this.federationManager.CurrentFederationKey.PubKey);
                    string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + myProposal.Value);

                    this.federatedPegBroadcaster.BroadcastAsync(new FeeProposalPayload(requestId, myProposal.Value, signature)).GetAwaiter().GetResult();
                }

                return isProposalConcluded;
            }
        }

        /// <inheritdoc/>
        public void ProposeFeeFromMultiSigMember(string requestId, ulong feeAmount, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                // If the request id has no proposals, add it.
                if (!this.feeProposalsByRequestId.TryGetValue(requestId, out Dictionary<PubKey, ulong> proposals))
                {
                    // Add this pubkey's proposal.
                    this.logger.Debug($"Request doesn't exist, adding proposal fee of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                    this.feeProposalsByRequestId.Add(requestId, new Dictionary<PubKey, ulong>() { { pubKey, feeAmount } });
                }
                else
                {
                    if (!proposals.Any(p => p.Key == pubKey))
                    {
                        proposals.Add(pubKey, feeAmount);
                        this.logger.Debug($"Request exists, adding proposal fee of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                    }
                    else
                        this.logger.Debug($"Conversion request id '{requestId}' has already been voted on by {pubKey}.");
                }

                this.feeProposalsByRequestId.TryGetValue(requestId, out proposals);

                // Check if this node has this request.
                if (!proposals.Any(p => p.Key == this.federationManager.CurrentFederationKey.PubKey))
                {
                    proposals.Add(this.federationManager.CurrentFederationKey.PubKey, feeAmount);

                    this.logger.Debug($"Adding proposal fee of {feeAmount} for conversion request id '{requestId}' from {pubKey} to this node.");
                }
            }
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

        public async Task BroadcastVoteAsync(Key federationKey,string requestId, ulong fee)
        {
            string signature = federationKey.SignMessage(requestId + fee);
            await this.federatedPegBroadcaster.BroadcastAsync(new FeeCoordinationPayload(requestId, fee, signature)).ConfigureAwait(false);
        }
    }
}
