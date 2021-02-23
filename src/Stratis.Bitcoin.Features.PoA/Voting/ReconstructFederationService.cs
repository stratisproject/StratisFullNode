using System;
using System.IO;
using System.Linq;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Configuration;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public sealed class ReconstructFederationService
    {
        private const int ReconstructionHeight = 1_410_000;

        private readonly IFederationManager federationManager;
        private readonly IIdleFederationMembersKicker idleFederationMembersKicker;
        private readonly object locker;
        private readonly Logger logger;
        private readonly NodeSettings nodeSettings;
        private readonly PoAConsensusOptions poaConsensusOptions;
        private readonly VotingManager votingManager;
        private bool isBusyReconstructing;

        public ReconstructFederationService(
            IFederationManager federationManager,
            NodeSettings nodeSettings,
            Network network,
            IIdleFederationMembersKicker idleFederationMembersKicker,
            VotingManager votingManager)
        {
            this.federationManager = federationManager;
            this.idleFederationMembersKicker = idleFederationMembersKicker;
            this.nodeSettings = nodeSettings;
            this.votingManager = votingManager;

            this.locker = new object();
            this.logger = LogManager.GetCurrentClassLogger();
            this.poaConsensusOptions = (PoAConsensusOptions)network.Consensus.Options;
        }

        public void Reconstruct()
        {
            if (!this.poaConsensusOptions.VotingEnabled)
            {
                this.logger.Warn("Voting is not enabled on this node.");
                return;
            }

            if (this.isBusyReconstructing)
            {
                this.logger.Info($"Reconstruction of the federation is already underway.");
                return;
            }

            lock (this.locker)
            {
                try
                {
                    this.isBusyReconstructing = true;

                    // First delete all polls that was started on or after the given height.
                    this.logger.Info($"Reconstructing voting data: Cleaning polls after height {ReconstructionHeight}");
                    this.votingManager.DeletePollsAfterHeight(ReconstructionHeight);

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
                    this.votingManager.ReconstructVotingDataFromHeightLocked(ReconstructionHeight);

                    this.logger.Info($"Reconstruction completed");

                    SetReconstructionFlag(false);
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

        public void SetReconstructionFlag(bool reconstructOnStartup)
        {
            string[] configLines = File.ReadAllLines(this.nodeSettings.ConfigurationFile);

            if (configLines.Any(c => c.Contains(PoAFeature.ReconstructFederationFlag)))
            {
                for (int i = 0; i < configLines.Length; i++)
                {
                    if (configLines[i].Contains(PoAFeature.ReconstructFederationFlag))
                        configLines[i] = $"{PoAFeature.ReconstructFederationFlag}={reconstructOnStartup}";
                }

                File.WriteAllLines(this.nodeSettings.ConfigurationFile, configLines);
            }
            else
            {
                using (StreamWriter sw = File.AppendText(this.nodeSettings.ConfigurationFile))
                {
                    sw.WriteLine($"{PoAFeature.ReconstructFederationFlag}={reconstructOnStartup}");
                };
            }
        }
    }
}
