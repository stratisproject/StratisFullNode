using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;
using NLog;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonConverters;
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

        void RegisterQuorumSize(int quorum);

        int GetQuorum();

        Task<InteropConversionRequestFee> AgreeFeeForConversionRequestAsync(string requestId, int blockHeight);

        Task MultiSigMemberProposedInteropFeeAsync(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey);

        Task MultiSigMemberAgreedOnInteropFeeAsync(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey);
    }

    public sealed class CoordinationManager : ICoordinationManager
    {
        private readonly IDateTimeProvider dateTimeProvider;
        private readonly IExternalApiPoller externalApiPoller;
        private readonly IFederationManager federationManager;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly IInteropFeeCoordinationKeyValueStore interopRequestKeyValueStore;
        private readonly INodeLifetime nodeLifetime;
        private readonly ILogger logger;

        /// <summary> Interflux transaction ID votes </summary>
        private Dictionary<string, Dictionary<BigInteger, int>> activeVotes;
        private Dictionary<string, HashSet<PubKey>> receivedVotes;

        /// <summary> Proposed fees by request id. </summary>
        private Dictionary<string, List<InterOpFeeToMultisig>> feeProposalsByRequestId;

        /// <summary> Agreed fees by request id. </summary>
        private Dictionary<string, List<InterOpFeeToMultisig>> agreedFeeVotesByRequestId;

        private int quorum;

        private readonly object lockObject = new object();

        public CoordinationManager(
            IDateTimeProvider dateTimeProvider,
            IExternalApiPoller externalApiPoller,
            IFederationManager federationManager,
            IFederatedPegBroadcaster federatedPegBroadcaster,
            IInteropFeeCoordinationKeyValueStore interopRequestKeyValueStore,
            INodeLifetime nodeLifetime,
            INodeStats nodeStats)
        {
            this.dateTimeProvider = dateTimeProvider;
            this.activeVotes = new Dictionary<string, Dictionary<BigInteger, int>>();
            this.receivedVotes = new Dictionary<string, HashSet<PubKey>>();

            this.feeProposalsByRequestId = new Dictionary<string, List<InterOpFeeToMultisig>>();
            this.agreedFeeVotesByRequestId = new Dictionary<string, List<InterOpFeeToMultisig>>();

            this.externalApiPoller = externalApiPoller;
            this.federationManager = federationManager;
            this.federatedPegBroadcaster = federatedPegBroadcaster;
            this.interopRequestKeyValueStore = interopRequestKeyValueStore;
            this.nodeLifetime = nodeLifetime;
            this.logger = LogManager.GetCurrentClassLogger();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
        }

        /// <inheritdoc/>
        public async Task<InteropConversionRequestFee> AgreeFeeForConversionRequestAsync(string requestId, int blockHeight)
        {
            InteropConversionRequestFee interopConversionRequestFee = null;

            DateTime lastConversionRequestSync = this.dateTimeProvider.GetUtcNow();
            DateTime conversionRequestSyncStart = this.dateTimeProvider.GetUtcNow();

            do
            {
                if (conversionRequestSyncStart.AddMinutes(3) <= this.dateTimeProvider.GetUtcNow())
                {
                    this.logger.Warn($"A fee for request '{requestId}' failed to reach consensus after 3 minutes... ignoring.");
                    break;
                }

                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    break;

                // Execute a small delay to not flood the network with proposal requests.
                if (lastConversionRequestSync.AddMilliseconds(500) > this.dateTimeProvider.GetUtcNow())
                    continue;

                lock (this.lockObject)
                {
                    interopConversionRequestFee = GetOrCreateInteropConversionRequestFeeLocked(requestId, blockHeight);
                }

                // If the fee proposal has not concluded then continue until it has.
                if (interopConversionRequestFee.State == InteropFeeState.ProposalInProgress)
                    await SubmitProposalForInteropFeeForConversionRequestAsync(interopConversionRequestFee);

                if (interopConversionRequestFee.State == InteropFeeState.AgreeanceInProgress)
                    await AgreeOnInteropFeeForConversionRequestAsync(interopConversionRequestFee);

                if (interopConversionRequestFee.State == InteropFeeState.AgreeanceConcluded)
                    break;

                lastConversionRequestSync = this.dateTimeProvider.GetUtcNow();

            } while (true);

            return interopConversionRequestFee;
        }

        private InteropConversionRequestFee GetOrCreateInteropConversionRequestFeeLocked(string requestId, int blockHeight)
        {
            InteropConversionRequestFee interopConversionRequest;

            byte[] proposalBytes = this.interopRequestKeyValueStore.LoadBytes(requestId);
            if (proposalBytes != null)
            {
                string json = Encoding.ASCII.GetString(proposalBytes);
                interopConversionRequest = Serializer.ToObject<InteropConversionRequestFee>(json);
            }
            else
            {
                interopConversionRequest = new InteropConversionRequestFee() { RequestId = requestId, BlockHeight = blockHeight, State = InteropFeeState.ProposalInProgress };
                this.interopRequestKeyValueStore.SaveValueJson(requestId, interopConversionRequest);

                this.logger.Debug($"InteropConversionRequestFee object for request '{requestId}' has been created.");
            }

            return interopConversionRequest;
        }

        private async Task SubmitProposalForInteropFeeForConversionRequestAsync(InteropConversionRequestFee interopConversionRequestFee)
        {
            List<InterOpFeeToMultisig> proposals = null;

            lock (this.lockObject)
            {
                // If the request id doesn't exist, propose the fee and broadcast it.
                if (!this.feeProposalsByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out proposals))
                {
                    ulong candidateFee = (ulong)(this.externalApiPoller.EstimateConversionTransactionFee() * 100_000_000m);

                    this.logger.Debug($"No nodes has proposed a fee of {candidateFee} for conversion request id '{interopConversionRequestFee.RequestId}'.");
                    this.feeProposalsByRequestId.Add(interopConversionRequestFee.RequestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee } });
                }
                else
                {
                    if (!HasFeeProposalBeenConcluded(interopConversionRequestFee) && !proposals.Any(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex()))
                    {
                        ulong candidateFee = (ulong)(this.externalApiPoller.EstimateConversionTransactionFee() * 100_000_000m);

                        this.logger.Debug($"Adding proposed fee of {candidateFee} for conversion request id '{interopConversionRequestFee.RequestId}'.");
                        proposals.Add(new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee });
                    }
                }

                this.feeProposalsByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out proposals);

                this.logger.Debug($"{proposals.Count} node(s) has proposed a fee for conversion request id {interopConversionRequestFee.RequestId}.");

                if (HasFeeProposalBeenConcluded(interopConversionRequestFee))
                {
                    // Only update the proposal state if it is ProposalInProgress and save it.
                    if (interopConversionRequestFee.State == InteropFeeState.ProposalInProgress)
                    {
                        interopConversionRequestFee.State = InteropFeeState.AgreeanceInProgress;
                        this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);

                        IEnumerable<long> values = proposals.Select(s => Convert.ToInt64(s.FeeAmount));
                        this.logger.Debug($"Proposal fee for request id '{interopConversionRequestFee.RequestId}' has concluded, average amount: {values.Average()}");
                    }
                }
            }

            InterOpFeeToMultisig myProposal = proposals.First(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
            string signature = this.federationManager.CurrentFederationKey.SignMessage(interopConversionRequestFee.RequestId + myProposal.FeeAmount);

            await this.federatedPegBroadcaster.BroadcastAsync(new FeeProposalPayload(interopConversionRequestFee.RequestId, myProposal.FeeAmount, interopConversionRequestFee.BlockHeight, signature));
        }

        /// <inheritdoc/>
        public async Task MultiSigMemberProposedInteropFeeAsync(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                InteropConversionRequestFee interopConversionRequestFee = GetOrCreateInteropConversionRequestFeeLocked(requestId, blockHeight);

                if (!HasFeeProposalBeenConcluded(interopConversionRequestFee))
                {
                    // If the request id has no proposals, add it.
                    if (!this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                    {
                        // Add this pubkey's proposal.
                        this.logger.Debug($"Conversion request proposal '{requestId}' received from pubkey '{pubKey}' which doesn't exist, adding proposal fee of {feeAmount}.");
                        this.feeProposalsByRequestId.Add(requestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount } });
                    }
                    else
                    {
                        if (!proposals.Any(p => p.PubKey == pubKey.ToHex()))
                        {
                            proposals.Add(new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount });
                            this.logger.Debug($"Conversion request proposal '{requestId}' received from pubkey '{pubKey}' which exists, adding proposal fee of {feeAmount}.");
                        }
                    }
                }
                else
                {
                    // Set the proposal to concluded only if it is ProposalInProgress
                    if (interopConversionRequestFee.State == InteropFeeState.ProposalInProgress)
                    {
                        this.logger.Debug($"Conversion request proposal '{requestId}' received from pubkey '{pubKey}' has concluded, setting state to '{InteropFeeState.AgreeanceInProgress}'");

                        interopConversionRequestFee.State = InteropFeeState.AgreeanceInProgress;
                        this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);
                    }
                }
            }

            // Broadcast/ask for this request from other nodes as well
            string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + feeAmount);
            await this.federatedPegBroadcaster.BroadcastAsync(new FeeProposalPayload(requestId, feeAmount, blockHeight, signature));
        }

        private async Task AgreeOnInteropFeeForConversionRequestAsync(InteropConversionRequestFee interopConversionRequestFee)
        {
            List<InterOpFeeToMultisig> votes = null;

            lock (this.lockObject)
            {
                if (!this.feeProposalsByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out List<InterOpFeeToMultisig> proposals))
                {
                    this.logger.Error($"Fee proposal for request id '{interopConversionRequestFee.RequestId}' does not exist.");
                    return;
                }

                ulong candidateFee = (ulong)proposals.Select(s => Convert.ToInt64(s.FeeAmount)).Average();

                var interOpFeeToMultisig = new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee };

                // If the request id doesn't exist yet, create a fee vote and broadcast it.
                if (!this.agreedFeeVotesByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out votes))
                {
                    this.logger.Debug($"No nodes has voted on conversion request id '{interopConversionRequestFee.RequestId}' with a fee amount of {candidateFee}.");
                    this.agreedFeeVotesByRequestId.Add(interopConversionRequestFee.RequestId, new List<InterOpFeeToMultisig>() { interOpFeeToMultisig });
                }
                else
                {
                    // Add this node's vote if its missing and has not yet concluded.
                    if (!HasFeeVoteBeenConcluded(interopConversionRequestFee.RequestId) && !votes.Any(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex()))
                    {
                        this.logger.Debug($"Adding fee vote for conversion request id '{interopConversionRequestFee.RequestId}' for amount {candidateFee}.");
                        votes.Add(interOpFeeToMultisig);
                    }
                }

                this.agreedFeeVotesByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out votes);

                this.logger.Debug($"{votes.Count} node(s) has voted on a fee for conversion request id '{interopConversionRequestFee.RequestId}'.");

                if (HasFeeVoteBeenConcluded(interopConversionRequestFee.RequestId))
                {
                    // Update the amount and state only if it is AgreeanceInProgress and save it.
                    if (interopConversionRequestFee.State == InteropFeeState.AgreeanceInProgress)
                    {
                        interopConversionRequestFee.Amount = (ulong)votes.Select(s => Convert.ToInt64(s.FeeAmount)).Average();
                        interopConversionRequestFee.State = InteropFeeState.AgreeanceConcluded;
                        this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);

                        this.logger.Debug($"Voting on fee for request id '{interopConversionRequestFee.RequestId}' has concluded, amount: {interopConversionRequestFee.Amount}");
                    }
                }
            }

            // If the fee vote is not concluded, broadcast again.
            InterOpFeeToMultisig myVote = votes.First(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
            string signature = this.federationManager.CurrentFederationKey.SignMessage(interopConversionRequestFee.RequestId + myVote.FeeAmount);

            await this.federatedPegBroadcaster.BroadcastAsync(new FeeAgreePayload(interopConversionRequestFee.RequestId, myVote.FeeAmount, interopConversionRequestFee.BlockHeight, signature));
        }

        /// <inheritdoc/>
        public async Task MultiSigMemberAgreedOnInteropFeeAsync(string requestId, ulong feeAmount, int blockHeight, PubKey pubKey)
        {
            lock (this.lockObject)
            {
                InteropConversionRequestFee interopConversionRequestFee = GetOrCreateInteropConversionRequestFeeLocked(requestId, blockHeight);
                if (interopConversionRequestFee.State == InteropFeeState.ProposalInProgress)
                    return;

                if (!HasFeeVoteBeenConcluded(requestId))
                {
                    // If the request id has no votes, add it.
                    if (!this.agreedFeeVotesByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> votes))
                    {
                        if (!this.feeProposalsByRequestId.TryGetValue(requestId, out List<InterOpFeeToMultisig> proposals))
                        {
                            this.logger.Error($"Conversion request fee vote '{requestId}' received from pubkey '{pubKey}' does not have any corresponding proposals as of yet.");
                            return;
                        }

                        // Add this pubkey's vote.
                        this.logger.Debug($"Conversion request fee vote '{requestId}' received from pubkey '{pubKey}' which doesnt exist, adding vote of {feeAmount}.");
                        this.agreedFeeVotesByRequestId.Add(requestId, new List<InterOpFeeToMultisig>() { new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount } });
                    }
                    else
                    {
                        if (!votes.Any(p => p.PubKey == pubKey.ToHex()))
                        {
                            votes.Add(new InterOpFeeToMultisig() { BlockHeight = blockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount });
                            this.logger.Debug($"Conversion request fee vote '{requestId}' received from pubkey '{pubKey}' which exists, adding fee vote of {feeAmount}.");
                        }
                    }
                }
                else
                {
                    // Set the agreeance to concluded
                    if (interopConversionRequestFee.State == InteropFeeState.AgreeanceInProgress)
                    {
                        this.logger.Debug($"Conversion request fee vote '{requestId}' received from pubkey '{pubKey}' has concluded, setting state to '{InteropFeeState.AgreeanceConcluded}'");

                        interopConversionRequestFee.Amount = feeAmount;
                        interopConversionRequestFee.State = InteropFeeState.AgreeanceConcluded;
                        this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);
                    }
                }
            }

            // Broadcast/ask for this vote from other nodes as well
            string signature = this.federationManager.CurrentFederationKey.SignMessage(requestId + feeAmount);
            await this.federatedPegBroadcaster.BroadcastAsync(new FeeAgreePayload(requestId, feeAmount, blockHeight, signature));
        }

        private bool HasFeeProposalBeenConcluded(InteropConversionRequestFee interopConversionRequestFee)
        {
            if (this.feeProposalsByRequestId.TryGetValue(interopConversionRequestFee.RequestId, out List<InterOpFeeToMultisig> proposals))
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

        public void RegisterQuorumSize(int quorum)
        {
            this.quorum = quorum;
        }

        public int GetQuorum()
        {
            return this.quorum;
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> Interop Coordination Manager");
            benchLog.AppendLine();
            benchLog.AppendLine(">> Fee Proposals (last 10):");

            foreach (KeyValuePair<string, List<InterOpFeeToMultisig>> proposal in this.feeProposalsByRequestId.Take(10))
            {
                IEnumerable<long> values = proposal.Value.Select(s => Convert.ToInt64(s.FeeAmount));

                var state = proposal.Value.Count >= this.quorum ? "Concluded" : "In Progress";
                benchLog.AppendLine($"Height: {proposal.Value.First().BlockHeight}  Id: {proposal.Key} Proposals: {proposal.Value.Count} Fee (Avg): {new Money((long)values.Average())} State: {state}");
            }

            benchLog.AppendLine();
            benchLog.AppendLine(">> Fee Votes (last 10):");

            foreach (KeyValuePair<string, List<InterOpFeeToMultisig>> vote in this.agreedFeeVotesByRequestId.Take(10))
            {
                IEnumerable<long> values = vote.Value.Select(s => Convert.ToInt64(s.FeeAmount));

                var state = vote.Value.Count >= this.quorum ? "Concluded" : "In Progress";
                benchLog.AppendLine($"Height: {vote.Value.First().BlockHeight} Id: {vote.Key} Votes: {vote.Value.Count} Fee : {new Money((long)values.Average())} State: {state}");
            }

            benchLog.AppendLine();
        }
    }

    public sealed class InteropConversionRequestFee
    {
        [JsonProperty(PropertyName = "requestid")]
        public string RequestId { get; set; }

        [JsonProperty(PropertyName = "amount")]
        public ulong Amount { get; set; }

        [JsonProperty(PropertyName = "height")]
        public int BlockHeight { get; set; }

        [JsonProperty(PropertyName = "state")]
        public InteropFeeState State { get; set; }
    }

    public sealed class InterOpFeeToMultisig
    {
        [JsonProperty(PropertyName = "height")]
        public int BlockHeight { get; set; }

        [JsonProperty(PropertyName = "pubkey")]
        public string PubKey { get; set; }

        [JsonProperty(PropertyName = "fee")]
        public ulong FeeAmount { get; set; }
    }

    public enum InteropFeeState
    {
        ProposalInProgress,
        AgreeanceInProgress,
        AgreeanceConcluded,
    }
}
