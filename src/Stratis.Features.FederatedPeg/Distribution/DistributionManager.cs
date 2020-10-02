using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.EventBus;
using Stratis.Bitcoin.EventBus.CoreEvents;
using Stratis.Bitcoin.Signals;
using Stratis.Features.Collateral;

namespace Stratis.Features.FederatedPeg.Distribution
{
    public enum DistributionState
    {
        Started,
        Signing,
        Broadcasting,
        Finalised
    }

    /// <summary>
    /// Constructs the distribution transactions for signing by the federation collectively.
    /// Runs on the sidechain.
    /// </summary>
    public class DistributionManager : IDistributionManager
    {
        private readonly Network network;
        private readonly ChainIndexer chainIndexer;
        private readonly ISignals signals;
        private readonly ILogger logger;

        private SubscriptionToken blockConnectedSubscription;
        
        /// <summary>
        /// Tracks height on the sidechain of the last distribution orchestration attempt.
        /// </summary>
        private int lastDistributionHeight;

        /// <summary>
        /// Track state of last distribution (we need to ensure that a previous attempt has completed before initiating a new one).
        /// </summary>
        private DistributionState lastDistributionState;
        
        public DistributionManager(Network network, ChainIndexer chainIndexer, ISignals signals, ILoggerFactory loggerFactory, IDistributionStore distributionStore)
        {
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.signals = signals;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            this.blockConnectedSubscription = this.signals.Subscribe<BlockConnected>(this.OnBlockConnected);
        }

        /// <inheritdoc />
        public void Distribute(DistributionRecord record)
        {
            // Get the set of miners (more specifically, their pubkeys) of the blocks in the previous epoch
        }

        private void OnBlockConnected(BlockConnected blockConnected)
        {
            // First check for reward transactions that need to be distributed

            // Check for commitment height in the block - is it a distribution block or higher?
            var commitmentHeightEncoder = new CollateralHeightCommitmentEncoder(this.logger);

            int? commitmentHeight = commitmentHeightEncoder.DecodeCommitmentHeight(blockConnected.ConnectedBlock.Block.Transactions.First());

            // We trust the consensus rules to be kicking out blocks that are meant to have valid commitments.
            // So if there isn't one, there is a good reason (e.g. the sidechain is below the commitment activation height) and we can just ignore this block for distribution purposes.
            if (commitmentHeight == null)
                return;

            // Need to wait for the distribution store height to catch up to this received block so that we have all the 

            // If so, can we initiate a new distribution?
        }
    }
}
