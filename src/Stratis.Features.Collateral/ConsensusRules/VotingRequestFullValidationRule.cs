using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.PoA;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.PoA.Features.Voting;
using Stratis.Features.Collateral.CounterChain;

namespace Stratis.Features.Collateral.ConsensusRules
{
    public class VotingRequestFullValidationRule : FullValidationConsensusRule
    {
        private readonly IInitialBlockDownloadState ibdState;
        private readonly Network network;
        private readonly Network counterChainNetwork;
        private readonly JoinFederationRequestEncoder encoder;
        private readonly VotingManager votingManager;
        private readonly IFederationManager federationManager;

        public VotingRequestFullValidationRule(IInitialBlockDownloadState ibdState, Network network, CounterChainNetworkWrapper counterChainNetwork, IFederationManager federationManager, VotingManager votingManager) : base()
        {
            this.ibdState = ibdState;
            this.network = network;
            this.counterChainNetwork = counterChainNetwork.CounterChainNetwork;
            this.encoder = new JoinFederationRequestEncoder();
            this.federationManager = federationManager;
            this.votingManager = votingManager;
        }

        /// <summary>Checks that any voting requests that are present can be decoded and prohibits re-use of collateral addresses.</summary>
        /// <param name="context">See <see cref="RuleContext"/>.</param>
        /// <returns>The asynchronous task.</returns>
        public override Task RunAsync(RuleContext context)
        {
            if (this.ibdState.IsInitialBlockDownload())
            {
                this.Logger.LogTrace("(-)[SKIPPED_IN_IBD]");
                return Task.CompletedTask;
            }

            foreach (Transaction transaction in context.ValidationContext.BlockToValidate.Transactions)
            {
                CheckTransaction(transaction, context.ValidationContext.ChainedHeaderToValidate.Height);
            }

            return Task.CompletedTask;
        }

        public void CheckTransaction(Transaction transaction, int height)
        {
            if (transaction.IsCoinBase || transaction.IsCoinStake)
                return;

            // This will raise a consensus error if there is a voting request and it can't be decoded.
            JoinFederationRequest request = JoinFederationRequestBuilder.Deconstruct(transaction, this.encoder);
            if (request == null)
                return;

            if (this.federationManager.IsMultisigMember(request.PubKey))
            {
                // Can't cast votes in relation to a multisig member.
                this.Logger.LogTrace("(-)[INVALID_VOTING_ON_MULTISIG_MEMBER]");
                PoAConsensusErrors.InvalidVotingOnMultiSig.Throw();
            }

            // Check collateral amount.
            var collateralAmount = CollateralFederationMember.GetCollateralAmountForPubKey((PoANetwork)this.network, request.PubKey);

            if (request.CollateralAmount.ToDecimal(MoneyUnit.BTC) != collateralAmount)
            {
                this.Logger.LogTrace("(-)[INVALID_COLLATERAL_REQUIREMENT]");
                PoAConsensusErrors.InvalidCollateralRequirement.Throw();
            }

            // Prohibit re-use of collateral addresses.
            if (height >= ((PoAConsensusOptions)(this.network.Consensus.Options)).Release1100ActivationHeight)
            {
                Script script = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(request.CollateralMainchainAddress);
                string collateralAddress = script.GetDestinationAddress(this.counterChainNetwork).ToString();
                CollateralFederationMember owner = this.federationManager.CollateralAddressOwner(this.votingManager, VoteKey.AddFederationMember, collateralAddress);
                if (owner != null && owner.PubKey != request.PubKey)
                {
                    this.Logger.LogTrace("(-)[INVALID_COLLATERAL_REUSE]");
                    PoAConsensusErrors.VotingRequestInvalidCollateralReuse.Throw();
                }
            }
        }
    }
}
