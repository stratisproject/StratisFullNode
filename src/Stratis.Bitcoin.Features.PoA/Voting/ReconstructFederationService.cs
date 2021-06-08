using System;
using System.IO;
using System.Linq;
using NBitcoin;
using NLog;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.PoA.Voting
{
    public sealed class ReconstructFederationService
    {
        private readonly IFederationManager federationManager;
        private readonly IIdleFederationMembersKicker idleFederationMembersKicker;
        private readonly object locker;
        private readonly Logger logger;
        private readonly NodeSettings nodeSettings;
        private readonly ChainIndexer chainIndexer;
        private readonly PoAConsensusOptions poaConsensusOptions;
        private readonly VotingManager votingManager;
        private bool isBusyReconstructing;

        public ReconstructFederationService(
            IFederationManager federationManager,
            NodeSettings nodeSettings,
            ChainIndexer chainIndexer,
            Network network,
            IIdleFederationMembersKicker idleFederationMembersKicker,
            VotingManager votingManager)
        {
            this.federationManager = federationManager;
            this.idleFederationMembersKicker = idleFederationMembersKicker;
            this.nodeSettings = nodeSettings;
            this.chainIndexer = chainIndexer;
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

                    // Determine the reconstruction height.
                    this.logger.Info($"Reconstructing voting data: Determining the reconstruction height.");
                    var federationMemberActivationTime = ((PoAConsensusOptions)this.nodeSettings.Network.Consensus.Options).FederationMemberActivationTime ?? 0;
                    int reconstructionHeight;
                    if (this.chainIndexer.Tip.Header.Time < federationMemberActivationTime)
                    {
                        reconstructionHeight = this.chainIndexer.Tip.Height + 1;
                    }
                    else
                    {
                        reconstructionHeight = BinarySearch.BinaryFindFirst(x => this.chainIndexer.GetHeader(x).Header.Time >= federationMemberActivationTime, 0, this.chainIndexer.Tip.Height + 1);
                    }

                    // First delete all polls that was started on or after the given height.
                    this.logger.Info($"Reconstructing voting data: Cleaning polls after height {reconstructionHeight}.");
                    this.votingManager.DeletePollsAfterHeight(reconstructionHeight);

                    // Re-initialize the federation manager which will re-contruct the federation make-up
                    // up to the given height.
                    this.logger.Info($"Reconstructing voting data: Re-initializing federation members.");
                    this.federationManager.Initialize();

                    // Reconstruct polls per block which will rebuild the federation.
                    this.logger.Info($"Reconstructing voting data...");
                    this.votingManager.ReconstructVotingDataFromHeightLocked(reconstructionHeight);

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
