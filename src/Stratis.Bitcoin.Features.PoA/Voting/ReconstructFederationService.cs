using NBitcoin;
using NLog;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public sealed class ReconstructFederationService
    {
        private readonly IFederationManager federationManager;
        private readonly IIdleFederationMembersKicker idleFederationMembersKicker;
        private readonly Logger logger;
        private readonly PoAConsensusOptions poaConsensusOptions;
        private readonly VotingManager votingManager;

        public ReconstructFederationService(
            IFederationManager federationManager,
            Network network,
            IIdleFederationMembersKicker idleFederationMembersKicker,
            VotingManager votingManager)
        {
            this.federationManager = federationManager;
            this.idleFederationMembersKicker = idleFederationMembersKicker;
            this.votingManager = votingManager;

            this.logger = LogManager.GetCurrentClassLogger();
            this.poaConsensusOptions = (PoAConsensusOptions)network.Consensus.Options;
        }

        public void Reconstruct(int height)
        {
            if (!this.poaConsensusOptions.VotingEnabled)
                this.logger.Warn("Voting is not enabled on this node.");

            // First we delete all polls that was started after the given height.
            this.votingManager.DeletePollsAfterHeight(height);

            // Re-initialize the idle members kicker as we will be resetting the
            // last active times via the reconstruction events..
            this.idleFederationMembersKicker.InitializeFederationMemberLastActiveTime();

            //this.votingManager.Initialize(this.federationHistory, this.idleFederationMembersKicker);

            // Re-initialize the federation manager which will re-contruct the federation make-up
            // up to the given height.
            this.federationManager.Initialize();

            // TODO investigate
            //this.whitelistedHashesRepository.Initialize();

            // Reconstruct polls per block which will rebuild the federation.
            this.votingManager.ReconstructVotingDataFromHeight(height);

        }
    }
}
