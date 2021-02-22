using System;
using NBitcoin;
using NLog;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public sealed class ReconstructFederationService
    {
        private readonly IFederationManager federationManager;
        private readonly IIdleFederationMembersKicker idleFederationMembersKicker;
        private readonly object locker;
        private readonly Logger logger;
        private readonly PoAConsensusOptions poaConsensusOptions;
        private readonly VotingManager votingManager;
        private bool isBusyReconstructing;

        public ReconstructFederationService(
            IFederationManager federationManager,
            Network network,
            IIdleFederationMembersKicker idleFederationMembersKicker,
            VotingManager votingManager)
        {
            this.federationManager = federationManager;
            this.idleFederationMembersKicker = idleFederationMembersKicker;
            this.votingManager = votingManager;

            this.locker = new object();
            this.logger = LogManager.GetCurrentClassLogger();
            this.poaConsensusOptions = (PoAConsensusOptions)network.Consensus.Options;
        }

        public void Reconstruct(int height)
        {
            if (!this.poaConsensusOptions.VotingEnabled)
            {
                this.logger.Warn("Voting is not enabled on this node.");
                return;
            }

            if (this.isBusyReconstructing)
            {
                this.logger.Info($"Reconstructing the federation is already underway.");
                return;
            }

            lock (this.locker)
            {
                try
                {
                    this.isBusyReconstructing = true;

                    // First delete all polls that was started on or after the given height.
                    this.logger.Info($"Reconstructing voting data: Cleaning polls after height {height}");
                    this.votingManager.DeletePollsAfterHeight(height);

                    // Re-initialize the federation manager which will re-contruct the federation make-up
                    // up to the given height.
                    this.logger.Info($"Reconstructing voting data: Re-initializing federation members.");
                    this.federationManager.Initialize();

                    // Re-initialize the idle members kicker as we will be resetting the
                    // last active times via the reconstruction events.
                    this.logger.Info($"Reconstructing voting data: Re-initializing federation members last active times.");
                    this.idleFederationMembersKicker.InitializeFederationMemberLastActiveTime(this.federationManager.GetFederationMembers());

                    // Reconstruct polls per block which will rebuild the federation.
                    this.logger.Info($"Reconstructing voting data...");
                    this.votingManager.ReconstructVotingDataFromHeightLocked(height);
                }
                catch (Exception ex)
                {
                    this.logger.Error($"An exception occurred reconstructing the federation: {ex}");
                    throw ex;
                }
                finally
                {
                    this.isBusyReconstructing = false;
                }
            }
        }
    }
}
