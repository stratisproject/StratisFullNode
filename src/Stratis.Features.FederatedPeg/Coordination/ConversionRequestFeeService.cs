using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;
using NLog;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Utilities;
using Stratis.Bitcoin.Utilities.JsonConverters;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Payloads;

namespace Stratis.Features.FederatedPeg.Coordination
{
    /// <summary>
    /// Attempts to determine a dynamic fee for the incoming conversion request.
    /// <para>
    /// This fee will be paid to the multisig to cover the fees incurred during the conversion request.
    /// </para>
    /// </summary>
    public interface IConversionRequestFeeService
    {
        /// <summary>
        /// Starts the process of first proposing a fee and then voting on said fee for an interop conversion request.
        /// <para>
        /// When the <see cref="TargetChain.MaturedBlocksSyncManager"/> receives a transaction that contains a conversion request, it passes it to this
        /// method, effectively stalling the process until the multisig nodes has proposed and voted on a fee to pay back to the multisig.
        /// </para>
        /// <para>
        /// Each multisig node, first gets an estimated fee in STRAX from an external client API for the conversion request and then broadcasts it to all the other multisig nodes.
        /// Once the quorum amount <see cref="FederatedPegSettings.MultiSigM"/> of proposals has been received, an avergae value is taken and then submitted
        /// as a fee vote.
        /// Once the quorum amount <see cref="FederatedPegSettings.MultiSigM"/> of votes has been received, the fee amount is returned to the sync manager
        /// which then creates a deposit out of the agreed amount.
        /// </para>
        /// </summary>
        /// <param name="requestId">The request id which is also used as the deposit id.</param>
        /// <param name="blockHeight">The block height at which the deposit was received.</param>
        /// <returns>An object containing the state and agreed fee amount.</returns>
        Task<InteropConversionRequestFee> AgreeFeeForConversionRequestAsync(string requestId, int blockHeight);

        /// <summary>
        /// Processes a fee proposal payload from another multisig.
        /// <para>
        /// This checks that the node hasn't already proposed the fee amount and that it is within an acceptable range, <see cref="ConversionRequestCoordinationService.IsFeeWithinAcceptableRange(List{InterOpFeeToMultisig}, string, ulong, PubKey)"/>.
        /// </para>
        /// </summary>
        /// <param name="requestId">The deposit id associated with the conversion request.</param>
        /// <param name="feeAmount">The proposed fee amount (retrieved from the external api client.</param>
        /// <param name="pubKey">The pubkey of the node that proposed the fee.</param>
        /// 
        /// <returns>A fee proposal payload for THIS node.</returns>
        Task<FeeProposalPayload> MultiSigMemberProposedInteropFeeAsync(string requestId, ulong feeAmount, PubKey pubKey);

        /// <summary>
        /// Processes a fee vote payload from another multisig.
        /// <para>
        /// This checks that the node hasn't already voted on the fee.
        /// </para>
        /// </summary>
        /// <param name="requestId">The deposit id associated with the conversion request.</param>
        /// <param name="feeAmount">The node's view of the average fee amount.</param>
        /// <param name="pubKey">The pubkey of the node that voted on the fee.</param>
        /// 
        /// <returns>A fee vote payload for THIS node.</returns>
        Task<FeeAgreePayload> MultiSigMemberAgreedOnInteropFeeAsync(string requestId, ulong feeAmount, PubKey pubKey);
    }

    public sealed class ConversionRequestFeeService : IConversionRequestFeeService
    {
        /// <summary> The amount of acceptable range another node can propose a fee in.</summary>
        private const decimal FeeProposalRange = 0.1m;

        /// <summary> The fallback fee incase the nodes can't agree.</summary>
        public static readonly Money FallBackFee = Money.Coins(150);

        private readonly AsyncLock asyncLockObject = new AsyncLock();

        private readonly IDateTimeProvider dateTimeProvider;
        private readonly IExternalApiPoller externalApiPoller;
        private readonly IFederationManager federationManager;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly IConversionRequestFeeKeyValueStore interopRequestKeyValueStore;
        private readonly ILogger logger;
        private readonly INodeLifetime nodeLifetime;

        public ConversionRequestFeeService(
            IDateTimeProvider dateTimeProvider,
            IExternalApiPoller externalApiPoller,
            IFederationManager federationManager,
            IFederatedPegSettings federatedPegSettings,
            IFederatedPegBroadcaster federatedPegBroadcaster,
            IConversionRequestFeeKeyValueStore interopRequestKeyValueStore,
            INodeLifetime nodeLifetime,
            INodeStats nodeStats)
        {
            this.dateTimeProvider = dateTimeProvider;
            this.externalApiPoller = externalApiPoller;
            this.federationManager = federationManager;
            this.federatedPegBroadcaster = federatedPegBroadcaster;
            this.federatedPegSettings = federatedPegSettings;
            this.interopRequestKeyValueStore = interopRequestKeyValueStore;
            this.nodeLifetime = nodeLifetime;

            this.logger = LogManager.GetCurrentClassLogger();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name, 251);
        }

        /// <inheritdoc/>
        public async Task<InteropConversionRequestFee> AgreeFeeForConversionRequestAsync(string requestId, int blockHeight)
        {
            // First check if this request is older than max-reorg and if so, ignore.
            // TODO: Find away to ignore old requests...

            InteropConversionRequestFee interopConversionRequestFee = null;

            DateTime lastConversionRequestSync = this.dateTimeProvider.GetUtcNow();
            DateTime conversionRequestSyncStart = this.dateTimeProvider.GetUtcNow();

            do
            {
                if (conversionRequestSyncStart.AddMinutes(2) <= this.dateTimeProvider.GetUtcNow())
                {
                    this.logger.Warn($"A fee for conversion request '{requestId}' failed to reach consensus after 2 minutes, ignoring.");
                    interopConversionRequestFee.State = InteropFeeState.FailRevertToFallback;
                    this.interopRequestKeyValueStore.SaveValueJson(requestId, interopConversionRequestFee);
                    break;
                }

                if (this.nodeLifetime.ApplicationStopping.IsCancellationRequested)
                    break;

                // Execute a small delay to not flood the network with proposal requests.
                if (lastConversionRequestSync.AddMilliseconds(500) > this.dateTimeProvider.GetUtcNow())
                    continue;

                using (await this.asyncLockObject.LockAsync().ConfigureAwait(false))
                {
                    interopConversionRequestFee = CreateInteropConversionRequestFeeLocked(requestId, blockHeight);

                    // If the fee proposal has not concluded then continue until it has.
                    if (interopConversionRequestFee.State == InteropFeeState.ProposalInProgress)
                        await SubmitProposalForInteropFeeForConversionRequestLockedAsync(interopConversionRequestFee).ConfigureAwait(false);

                    if (interopConversionRequestFee.State == InteropFeeState.AgreeanceInProgress)
                        await AgreeOnInteropFeeForConversionRequestLockedAsync(interopConversionRequestFee).ConfigureAwait(false);

                    if (interopConversionRequestFee.State == InteropFeeState.AgreeanceConcluded)
                        break;
                }

                lastConversionRequestSync = this.dateTimeProvider.GetUtcNow();

            } while (true);

            return interopConversionRequestFee;
        }

        private InteropConversionRequestFee GetInteropConversionRequestFeeLocked(string requestId)
        {
            InteropConversionRequestFee interopConversionRequest = null;

            byte[] proposalBytes = this.interopRequestKeyValueStore.LoadBytes(requestId);
            if (proposalBytes != null)
            {
                string json = Encoding.ASCII.GetString(proposalBytes);
                interopConversionRequest = Serializer.ToObject<InteropConversionRequestFee>(json);
            }

            return interopConversionRequest;
        }

        private InteropConversionRequestFee CreateInteropConversionRequestFeeLocked(string requestId, int blockHeight)
        {
            InteropConversionRequestFee interopConversionRequest = GetInteropConversionRequestFeeLocked(requestId);
            if (interopConversionRequest == null)
            {
                interopConversionRequest = new InteropConversionRequestFee() { RequestId = requestId, BlockHeight = blockHeight, State = InteropFeeState.ProposalInProgress };
                this.interopRequestKeyValueStore.SaveValueJson(requestId, interopConversionRequest);
                this.logger.Debug($"InteropConversionRequestFee object for request '{requestId}' has been created.");
            }

            return interopConversionRequest;
        }

        /// <summary>
        /// Submits this node's fee proposal. This methods must be called from within <see cref="asyncLockObject"/>.
        /// </summary>
        /// <param name="interopConversionRequestFee">The request we are currently processing.</param>
        /// <returns>The awaited task.</returns>
        private async Task SubmitProposalForInteropFeeForConversionRequestLockedAsync(InteropConversionRequestFee interopConversionRequestFee)
        {
            // Check if this node has already proposed its fee.
            if (!interopConversionRequestFee.FeeProposals.Any(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex()))
            {
                if (!EstimateConversionTransactionFee(out ulong candidateFee))
                    return;

                interopConversionRequestFee.FeeProposals.Add(new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee });
                this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);

                this.logger.Debug($"Adding this node's proposal fee of {candidateFee} for conversion request id '{interopConversionRequestFee.RequestId}'.");
            }

            this.logger.Debug($"{interopConversionRequestFee.FeeProposals.Count} node(s) has proposed a fee for conversion request id '{interopConversionRequestFee.RequestId}'.");

            if (HasFeeProposalBeenConcluded(interopConversionRequestFee))
            {
                // Only update the proposal state if it is ProposalInProgress and save it.
                if (interopConversionRequestFee.State == InteropFeeState.ProposalInProgress)
                {
                    interopConversionRequestFee.State = InteropFeeState.AgreeanceInProgress;
                    this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);

                    IEnumerable<long> values = interopConversionRequestFee.FeeProposals.Select(s => Convert.ToInt64(s.FeeAmount));
                    this.logger.Debug($"Proposal fee for request id '{interopConversionRequestFee.RequestId}' has concluded, average amount: {values.Average()}");
                }
            }

            InterOpFeeToMultisig myProposal = interopConversionRequestFee.FeeProposals.First(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
            string signature = this.federationManager.CurrentFederationKey.SignMessage(interopConversionRequestFee.RequestId + myProposal.FeeAmount);

            await this.federatedPegBroadcaster.BroadcastAsync(FeeProposalPayload.Request(interopConversionRequestFee.RequestId, myProposal.FeeAmount, interopConversionRequestFee.BlockHeight, signature)).ConfigureAwait(false);
        }

        /// <summary>
        /// Submits this node's fee vote. This methods must be called from within <see cref="asyncLockObject"/>.
        /// </summary>
        /// <param name="interopConversionRequestFee">The request we are currently processing.</param>
        /// <returns>The awaited task.</returns>
        private async Task AgreeOnInteropFeeForConversionRequestLockedAsync(InteropConversionRequestFee interopConversionRequestFee)
        {
            if (!HasFeeProposalBeenConcluded(interopConversionRequestFee))
            {
                this.logger.Error($"Cannot vote on fee proposal for request id '{interopConversionRequestFee.RequestId}' as it hasn't concluded yet.");
                return;
            }

            // Check if this node has already vote on this fee.
            if (!interopConversionRequestFee.FeeVotes.Any(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex()))
            {
                ulong candidateFee = (ulong)interopConversionRequestFee.FeeProposals.Select(s => Convert.ToInt64(s.FeeAmount)).Average();

                var interOpFeeToMultisig = new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = this.federationManager.CurrentFederationKey.PubKey.ToHex(), FeeAmount = candidateFee };
                interopConversionRequestFee.FeeVotes.Add(interOpFeeToMultisig);
                this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);

                this.logger.Debug($"Creating fee vote for conversion request id '{interopConversionRequestFee.RequestId}' with a fee amount of {new Money(candidateFee)}.");
            }

            this.logger.Debug($"{interopConversionRequestFee.FeeVotes.Count} node(s) has voted on a fee for conversion request id '{interopConversionRequestFee.RequestId}'.");

            if (HasFeeVoteBeenConcluded(interopConversionRequestFee))
                ConcludeInteropConversionRequestFee(interopConversionRequestFee);

            // Broadcast this peer's vote to the federation
            InterOpFeeToMultisig myVote = interopConversionRequestFee.FeeVotes.First(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
            string signature = this.federationManager.CurrentFederationKey.SignMessage(interopConversionRequestFee.RequestId + myVote.FeeAmount);

            await this.federatedPegBroadcaster.BroadcastAsync(FeeAgreePayload.Request(interopConversionRequestFee.RequestId, myVote.FeeAmount, interopConversionRequestFee.BlockHeight, signature)).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<FeeProposalPayload> MultiSigMemberProposedInteropFeeAsync(string requestId, ulong feeAmount, PubKey pubKey)
        {
            using (await this.asyncLockObject.LockAsync().ConfigureAwait(false))
            {
                // If this node does not have the fee proposal, return and wait for the matured blocks sync manager to find and create it.
                InteropConversionRequestFee interopConversionRequestFee = GetInteropConversionRequestFeeLocked(requestId);
                if (interopConversionRequestFee == null)
                    return null;

                // Check if the incoming proposal has not yet concluded and that the node has not yet proposed it.
                if (!HasFeeProposalBeenConcluded(interopConversionRequestFee) && !interopConversionRequestFee.FeeProposals.Any(p => p.PubKey == pubKey.ToHex()))
                {
                    // Check that the fee proposal is in range.
                    if (!IsFeeWithinAcceptableRange(interopConversionRequestFee.FeeProposals, requestId, feeAmount, pubKey))
                        return null;

                    interopConversionRequestFee.FeeProposals.Add(new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount });
                    this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);

                    this.logger.Debug($"Received conversion request proposal '{requestId}' from '{pubKey}', proposing fee of {new Money(feeAmount)}.");
                }

                // This node would have proposed this fee if the InteropConversionRequestFee object exists.
                InterOpFeeToMultisig myProposal = interopConversionRequestFee.FeeProposals.First(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
                string signature = this.federationManager.CurrentFederationKey.SignMessage(interopConversionRequestFee.RequestId + myProposal.FeeAmount);
                return FeeProposalPayload.Reply(interopConversionRequestFee.RequestId, myProposal.FeeAmount, interopConversionRequestFee.BlockHeight, signature);
            }
        }

        /// <inheritdoc/>
        public async Task<FeeAgreePayload> MultiSigMemberAgreedOnInteropFeeAsync(string requestId, ulong feeAmount, PubKey pubKey)
        {
            using (await this.asyncLockObject.LockAsync().ConfigureAwait(false))
            {
                // If this node does not have this conversion request fee, return and wait for the matured blocks sync manager to find it.
                InteropConversionRequestFee interopConversionRequestFee = GetInteropConversionRequestFeeLocked(requestId);
                if (interopConversionRequestFee == null || (interopConversionRequestFee != null && interopConversionRequestFee.State == InteropFeeState.ProposalInProgress))
                    return null;

                // Check if the vote is still in progress and that the incoming node still has to vote on this fee.
                if (!HasFeeVoteBeenConcluded(interopConversionRequestFee) && !interopConversionRequestFee.FeeVotes.Any(p => p.PubKey == pubKey.ToHex()))
                {
                    interopConversionRequestFee.FeeVotes.Add(new InterOpFeeToMultisig() { BlockHeight = interopConversionRequestFee.BlockHeight, PubKey = pubKey.ToHex(), FeeAmount = feeAmount });
                    this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);

                    this.logger.Debug($"Received conversion request fee vote '{requestId}' from '{pubKey} for a fee of {new Money(feeAmount)}.");
                }

                // This node would have voted on this if the InteropConversionRequestFee object exists.
                InterOpFeeToMultisig myVote = interopConversionRequestFee.FeeVotes.FirstOrDefault(p => p.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToHex());
                if (myVote == null)
                    return null;

                string signature = this.federationManager.CurrentFederationKey.SignMessage(interopConversionRequestFee.RequestId + myVote.FeeAmount);
                return FeeAgreePayload.Reply(interopConversionRequestFee.RequestId, myVote.FeeAmount, interopConversionRequestFee.BlockHeight, signature);
            }
        }

        /// <summary>
        /// Check that the fee proposed is within range of the current average.
        /// </summary>
        /// <param name="proposals">The current set of fee proposals.</param>
        /// <param name="requestId">The request id in question.</param>
        /// <param name="feeAmount">The fee amount from the other node.</param>
        /// <param name="pubKey">The pubkey of the node proposing the fee.</param>
        /// <returns><c>true</c> if within range of <see cref="FeeProposalRange"/></c></returns>
        private bool IsFeeWithinAcceptableRange(List<InterOpFeeToMultisig> proposals, string requestId, ulong feeAmount, PubKey pubKey)
        {
            var currentAverage = (ulong)proposals.Select(s => Convert.ToInt64(s.FeeAmount)).Average();
            if (feeAmount < (currentAverage - (currentAverage * FeeProposalRange)) ||
                feeAmount > (currentAverage + (currentAverage * FeeProposalRange)))
            {
                this.logger.Debug($"Conversion request '{requestId}' received from pubkey '{pubKey}' with amount {feeAmount} is out of range of the current average of {currentAverage}, skipping.");
                return false;
            }

            return true;
        }

        private bool EstimateConversionTransactionFee(out ulong candidateFee)
        {
            candidateFee = 0;

            var conversionTransactionFee = this.externalApiPoller.EstimateConversionTransactionFee();
            if (conversionTransactionFee == -1)
            {
                this.logger.Debug("External poller returned -1, will retry.");
                return false;
            }

            candidateFee = (ulong)(conversionTransactionFee * 100_000_000m);

            return true;
        }

        private bool HasFeeProposalBeenConcluded(InteropConversionRequestFee interopConversionRequestFee)
        {
            return interopConversionRequestFee.FeeProposals.Count >= this.federatedPegSettings.MultiSigM;
        }

        private bool HasFeeVoteBeenConcluded(InteropConversionRequestFee interopConversionRequestFee)
        {
            return interopConversionRequestFee.FeeVotes.Count >= this.federatedPegSettings.MultiSigM;
        }

        private void ConcludeInteropConversionRequestFee(InteropConversionRequestFee interopConversionRequestFee)
        {
            if (interopConversionRequestFee.State != InteropFeeState.AgreeanceInProgress)
                return;

            foreach (InterOpFeeToMultisig vote in interopConversionRequestFee.FeeVotes)
            {
                this.logger.Debug($"Pubkey '{vote.PubKey}' voted for {new Money(vote.FeeAmount)}.");
            }

            // Try and find the majority vote
            IEnumerable<IGrouping<decimal, decimal>> grouped = interopConversionRequestFee.FeeVotes.Select(v => Math.Truncate(Money.Satoshis(v.FeeAmount).ToDecimal(MoneyUnit.BTC))).GroupBy(s => s);
            IGrouping<decimal, decimal> majority = grouped.OrderByDescending(g => g.Count()).First();
            if (majority.Count() >= (this.federatedPegSettings.MultiSigM / 2) + 1)
                interopConversionRequestFee.Amount = Money.Coins(majority.Key);
            else
                interopConversionRequestFee.Amount = FallBackFee;

            interopConversionRequestFee.State = InteropFeeState.AgreeanceConcluded;
            this.interopRequestKeyValueStore.SaveValueJson(interopConversionRequestFee.RequestId, interopConversionRequestFee, true);

            this.logger.Debug($"Voting on fee for request id '{interopConversionRequestFee.RequestId}' has concluded, amount: {new Money(interopConversionRequestFee.Amount)}");
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> InterFlux Fee Proposals (last 5):");

            IOrderedEnumerable<InteropConversionRequestFee> conversionRequests = this.interopRequestKeyValueStore.GetAllAsJson<InteropConversionRequestFee>().OrderByDescending(i => i.BlockHeight);
            foreach (InteropConversionRequestFee conversionRequest in conversionRequests.Take(5))
            {
                IEnumerable<long> proposals = conversionRequest.FeeProposals.Select(s => Convert.ToInt64(s.FeeAmount));
                IEnumerable<long> votes = conversionRequest.FeeVotes.Select(s => Convert.ToInt64(s.FeeAmount));

                Money averageProposal = proposals.Any() ? new Money((long)proposals.Average()) : 0;

                benchLog.AppendLine($"Height: {conversionRequest.BlockHeight} Id: {conversionRequest.RequestId} Proposals: {conversionRequest.FeeProposals.Count} Proposal Amount (Avg): {averageProposal} Votes: {conversionRequest.FeeVotes.Count} Amount: {new Money(conversionRequest.Amount)} State: {conversionRequest.State}");
            }

            benchLog.AppendLine();
        }
    }

    public sealed class InteropConversionRequestFee
    {
        public InteropConversionRequestFee()
        {
            this.FeeProposals = new List<InterOpFeeToMultisig>();
            this.FeeVotes = new List<InterOpFeeToMultisig>();
        }

        [JsonProperty(PropertyName = "requestid")]
        public string RequestId { get; set; }

        [JsonProperty(PropertyName = "amount")]
        public ulong Amount { get; set; }

        [JsonProperty(PropertyName = "height")]
        public int BlockHeight { get; set; }

        [JsonProperty(PropertyName = "state")]
        public InteropFeeState State { get; set; }

        [JsonProperty(PropertyName = "proposals")]
        public List<InterOpFeeToMultisig> FeeProposals { get; set; }

        [JsonProperty(PropertyName = "votes")]
        public List<InterOpFeeToMultisig> FeeVotes { get; set; }
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
        ProposalInProgress = 0,
        AgreeanceInProgress = 1,
        AgreeanceConcluded = 2,
        FailRevertToFallback = 3
    }
}
