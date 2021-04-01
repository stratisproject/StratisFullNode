using NBitcoin;
using Stratis.Bitcoin.Configuration.Settings;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Base
{
    /// <summary>
    /// Provides IBD (Initial Block Download) state.
    /// </summary>
    /// <seealso cref="IInitialBlockDownloadState" />
    public class InitialBlockDownloadState : IInitialBlockDownloadState
    {
        /// <summary>A provider of the date and time.</summary>
        private readonly IDateTimeProvider dateTimeProvider;

        /// <summary>Provider of block header hash checkpoints.</summary>
        private readonly ICheckpoints checkpoints;

        /// <summary>Information about node's chain.</summary>
        private readonly IChainState chainState;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        private readonly Network network;

        /// <summary>User defined consensus settings.</summary>
        private readonly ConsensusSettings consensusSettings;

        private int lastCheckpointHeight;
        private uint256 minimumChainWork;

        public InitialBlockDownloadState(IChainState chainState, Network network, ConsensusSettings consensusSettings, ICheckpoints checkpoints, IDateTimeProvider dateTimeProvider)
        {
            Guard.NotNull(chainState, nameof(chainState));

            this.network = network;
            this.consensusSettings = consensusSettings;
            this.chainState = chainState;
            this.checkpoints = checkpoints;
            this.dateTimeProvider = dateTimeProvider;

            this.lastCheckpointHeight = this.checkpoints.GetLastCheckpointHeight();
            this.minimumChainWork = this.network.Consensus.MinimumChainWork ?? uint256.Zero;
        }

        /// <inheritdoc />
        public bool IsInitialBlockDownload()
        {
            if (this.chainState.ConsensusTip == null)
                return true;

            if (this.lastCheckpointHeight > this.chainState.ConsensusTip.Height)
                return true;

            if (this.chainState.ConsensusTip.Header.BlockTime < (this.dateTimeProvider.GetUtcNow().AddSeconds(-this.consensusSettings.MaxTipAge)))
            {
                if (!this.network.IsRegTest())
                    return true;

                // RegTest networks may experience long periods of no mining.
                // If this happens we don't want to be in IBD because we can't
                // mine new blocks in IBD and our nodes will be frozen at the
                // current height. Also note that in RegTest its typical for one
                // machine to control all (local) nodes and hence we are in control
                // of any side-effects that may arise from returning false here.
            }

            if (this.chainState.ConsensusTip.ChainWork < this.minimumChainWork)
                return true;

            return false;
        }
    }
}
