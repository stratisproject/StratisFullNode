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

        bool ProposeFeeForConversionRequest(string requestId, int blockHeight);

        bool AgreeFeeForConversionRequest(string requestId, int blockHeight, out ulong agreedFeeAmount);

        void MultiSigMemberProposedFee(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey);

        void MultiSigMemberAgreedOnFee(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey);
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
        private Dictionary<string, List<InterOpFeeToMultisig>> feeProposalsByRequestId;
        private Dictionary<string, List<InterOpFeeToMultisig>> agreedFeeVotesByRequestId;

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

            this.feeProposalsByRequestId = new Dictionary<string, List<InterOpFeeToMultisig>>();
            this.agreedFeeVotesByRequestId = new Dictionary<string, List<InterOpFeeToMultisig>>();

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
            benchLog.AppendLine();
            benchLog.AppendLine(">> Fee Proposals (last 5):");

            foreach (KeyValuePair<string, List<InterOpFeeToMultisig>> proposal in this.feeProposalsByRequestId.Take(5))
            {
                IEnumerable<long> values = proposal.Value.Select(s => Convert.ToInt64(s.FeeAmount));

                var state = proposal.Value.Count >= this.quorum ? "Concluded" : "In Progress";
                benchLog.AppendLine($"Height: {proposal.Value.First().BlockHeight}  Id: {proposal.Key} Proposals: {proposal.Value.Count} Fee (Avg): {new Money((long)values.Average())} State: {state}");
            }

            benchLog.AppendLine();
            benchLog.AppendLine(">> Fee Votes (last 5):");

            foreach (KeyValuePair<string, List<InterOpFeeToMultisig>> vote in this.agreedFeeVotesByRequestId.Take(5))
            {
                IEnumerable<long> values = vote.Value.Select(s => Convert.ToInt64(s.FeeAmount));

                var state = vote.Value.Count >= this.quorum ? "Concluded" : "In Progress";
                benchLog.AppendLine($"Height: {vote.Value.First().BlockHeight} Id: {vote.Key} Votes: {vote.Value.Count} Fee (Avg): {new Money((long)values.Average())} State: {state}");
            }

            benchLog.AppendLine();
        }

        /// <inheritdoc/>
        public bool ProposeFeeForConversionRequest(string requestId, int blockHeight)
        {
            lock (this.lockObject)
            {
                bool isProposalConcluded = false;

                // If the request id doesn't exist yet, propose the fee and broadcast it.
                if (!this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                {
                    ulong candidateFee = (ulong)(this.externalApiPoller.EstimateConversionTransactionFee() * 100_000_000m);

                    this.logger.Debug($"No nodes has proposed a fee of {candidateFee} for conversion request id '{requestId}'.");

                    this.feeProposalsByRequestId.Add(requestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee } });
                }
                else
                {
                    if (!proposals.Any(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex()))
                    {
                        ulong candidateFee = (ulong)(this.externalApiPoller.EstimateConversionTransactionFee() * 100_000_000m);

                        this.logger.Debug($"Adding proposed fee of {candidateFee} for conversion request id '{requestId}'.");
                        proposals.Add(new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee });
                    }
                    else
                        this.logger.Debug($"This node has already proposed a fee for conversion request id '{requestId}'.");
                }

                this.feeProposalsByRequestId.TryGetValue(requestId, out proposals);

                this.logger.Debug($"{proposals.Count} node(s) has proposed a fee for conversion request id {requestId}.");

                if (HasFeeProposalBeenConcluded(requestId))
                {
                    IEnumerable<long> values = proposals.Select(s => Convert.ToInt64(s.FeeAmount));
                    this.logger.Debug($"Proposal fee for request id '{requestId}' has concluded, average amount: {values.Average()}");

                    isProposalConcluded = true;
                }
                else
                {
                    // If the proposal is not concluded, broadcast again.
                    InterOpFeeToMultisig myProposal = proposals.First(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
                    string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + myProposal.FeeAmount);

                    this.federatedPegBroadcaster.BroadcastAsync(new FeeProposalPayload(requestId, myProposal.FeeAmount, blockHeight, signature)).GetAwaiter().GetResult();
                }

                return isProposalConcluded;
            }
        }

        /// <inheritdoc/>
        public void MultiSigMemberProposedFee(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                // If the request id has no proposals, add it.
                if (!this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                {
                    // Add this pubkey's proposal.
                    this.logger.Debug($"Request doesn't exist, adding proposal fee of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                    this.feeProposalsByRequestId.Add(requestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount } });
                }
                else
                {
                    if (!proposals.Any(p => p.PubKey == pubKey.ToHex()))
                    {
                        proposals.Add(new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount });
                        this.logger.Debug($"Request exists, adding proposal fee of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                    }
                    else
                        this.logger.Debug($"Conversion request id '{requestId}' has already been proposed by {pubKey}.");
                }

                this.feeProposalsByRequestId.TryGetValue(requestId, out proposals);

                // TODO Rethink this.
                Task.Delay(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();

                // Broadcast/ask for this request from other nodes as well
                string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + feeAmount);
                this.federatedPegBroadcaster.BroadcastAsync(new FeeProposalPayload(requestId, feeAmount, blockHeight, signature)).GetAwaiter().GetResult();
            }
        }

        /// <inheritdoc/>
        public bool AgreeFeeForConversionRequest(string requestId, int blockHeight, out ulong agreedFeeAmount)
        {
            lock (this.lockObject)
            {
                bool isFeeAgreedUpon = false;
                agreedFeeAmount = 0;

                // If the request id doesn't exist yet, create a fee vote and broadcast it.
                if (!this.agreedFeeVotesByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> votes))
                {
                    if (!this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                    {
                        this.logger.Error($"Fee proposal for request id '{requestId}' does not exist.");
                        return false;
                    }

                    ulong candidateFee = (ulong)proposals.Select(s => Convert.ToInt64(s.FeeAmount)).Average();

                    this.logger.Debug($"No nodes has voted on conversion request id '{requestId}' with a fee amount of {candidateFee}.");

                    this.agreedFeeVotesByRequestId.Add(requestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee } });
                }
                else
                {
                    if (!votes.Any(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex()))
                    {
                        // TODO: duplicate code
                        if (!this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                        {
                            this.logger.Error($"Fee proposal for request id '{requestId}' does not exist.");
                            return false;
                        }

                        ulong candidateFee = (ulong)proposals.Select(s => Convert.ToInt64(s.FeeAmount)).Average();

                        this.logger.Debug($"Adding fee vote for conversion request id '{requestId}' for amount {candidateFee}.");
                        votes.Add(new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee });
                    }
                    else
                        this.logger.Debug($"This node has already voted on conversion request id '{requestId}'.");
                }

                this.agreedFeeVotesByRequestId.TryGetValue(requestId, out votes);

                this.logger.Debug($"{votes.Count} node(s) has voted on a fee for conversion request id '{requestId}'.");

                if (HasFeeVoteBeenConcluded(requestId))
                {
                    this.logger.Debug($"Voting on fee for request id '{requestId}' has concluded, amount: {votes.Select(s => Convert.ToInt64(s.FeeAmount)).First()}");
                    agreedFeeAmount = votes.First().FeeAmount;

                    isFeeAgreedUpon = true;
                }
                else
                {
                    // If the fee vote is not concluded, broadcast again.
                    InterOpFeeToMultisig myVote = votes.First(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
                    string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + myVote.FeeAmount);

                    this.federatedPegBroadcaster.BroadcastAsync(new FeeAgreePayload(requestId, myVote.FeeAmount, blockHeight, signature)).GetAwaiter().GetResult();
                }

                return isFeeAgreedUpon;
            }
        }

        /// <inheritdoc/>
        public void MultiSigMemberAgreedOnFee(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                // If the request id has no votes, add it.
                if (!this.agreedFeeVotesByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> votes))
                {
                    if (!this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                    {
                        this.logger.Error($"Fee proposal for request id '{requestId}' does not exist.");
                        return;
                    }

                    // Add this pubkey's vote.
                    this.logger.Debug($"Fee vote doesn't exist, adding vote of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                    this.agreedFeeVotesByRequestId.Add(requestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount } });
                }
                else
                {
                    if (!votes.Any(p => p.PubKey == pubKey.ToHex()))
                    {
                        votes.Add(new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount });
                        this.logger.Debug($"Request exists, adding fee vote of {feeAmount} for conversion request id '{requestId}' from {pubKey}.");
                    }
                    else
                        this.logger.Debug($"Conversion request id '{requestId}' has already been voted on by {pubKey}.");
                }

                this.agreedFeeVotesByRequestId.TryGetValue(requestId, out votes);

                // TODO Rethink this.
                Task.Delay(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();

                // Broadcast/ask for this vote from other nodes as well
                string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + feeAmount);
                this.federatedPegBroadcaster.BroadcastAsync(new FeeAgreePayload(requestId, feeAmount, blockHeight, signature)).GetAwaiter().GetResult();
            }
        }

        private bool HasFeeProposalBeenConcluded(string requestId)
        {
            if (this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                return proposals.Count >= this.quorum;

            return false;
        }

        private bool HasFeeVoteBeenConcluded(string requestId)
        {
            if (this.agreedFeeVotesByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> votes))
                return votes.Count >= this.quorum;

            return false;
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

    public sealed class InterOpFeeToMultisig
    {
        public int BlockHeight { get; set; }
        public string PubKey { get; set; }
        public ulong FeeAmount { get; set; }
    }
}
