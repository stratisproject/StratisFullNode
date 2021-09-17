using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Base.Deployments
{
    /// <summary>
    /// Determines the activation height of a given deployment.
    /// Also used to determine if a deployment was active at a given height.
    /// </summary>
    public class ActivationHeightProvider
    {
        private Network network;
        private ChainIndexer chainIndexer;
        private ThresholdConditionCache cache;
        private int deployment;
        private ChainedHeader lastPollExpiryHeightChecked;
        private int pollExpiryActivationHeight;

        public ActivationHeightProvider(Network network, ThresholdConditionCache cache, ChainIndexer chainIndexer, int deployment)
        {
            this.network = network;
            this.cache = cache;
            this.chainIndexer = chainIndexer;
            this.deployment = deployment;
            this.lastPollExpiryHeightChecked = null;
            this.pollExpiryActivationHeight = int.MaxValue;
        }

        /// <summary>
        /// Looks up to "MinerConfirmationWindow" blocks beyond the current chain tip whether a given deployment is active or will become active.
        /// </summary>
        /// <returns><c>int.MaxValue</c> if the deployment is not active and the activation height can't be determined otherwise returns the activation height.</returns>
        public int ActivationHeight
        {
            get
            {
                if (this.pollExpiryActivationHeight == int.MaxValue && this.chainIndexer.Tip != this.lastPollExpiryHeightChecked)
                {
                    if (this.lastPollExpiryHeightChecked != null)
                        this.lastPollExpiryHeightChecked = this.chainIndexer.Tip.FindFork(this.lastPollExpiryHeightChecked);

                    int lastHeightChecked = (this.lastPollExpiryHeightChecked == null) ? 0 : this.lastPollExpiryHeightChecked.Height;
                    int activeHeight = BinarySearch.BinaryFindFirst((h) => this.IsLockedInAtHeight(h), lastHeightChecked + 1, this.chainIndexer.Tip.Height - lastHeightChecked);
                    this.lastPollExpiryHeightChecked = this.chainIndexer.Tip;

                    if (activeHeight >= 0)
                        this.pollExpiryActivationHeight = this.IsActiveAtHeight(activeHeight) ? activeHeight : (activeHeight + this.network.Consensus.MinerConfirmationWindow);
                }

                return this.pollExpiryActivationHeight;
            }
        }

        /// <summary>
        /// For a given height determines if a deployment is active or is locked in and will be active.
        /// </summary>
        /// <returns><c>true</c> if the deployment is active or is in a state where it will be active and <c>false</c> otherwise.</returns>
        public bool IsActiveAtHeight(int height)
        {
            int expectedLockedInHeight = height - this.network.Consensus.MinerConfirmationWindow;
            if (expectedLockedInHeight > 0)
                return this.IsLockedInAtHeight(expectedLockedInHeight);

            ThresholdState state = this.cache.GetState(this.chainIndexer.GetHeader(height).Previous, this.deployment);
            return state == ThresholdState.Active;
        }

        /// <summary>
        /// For a given height determines if a deployment is either locked in or active.
        /// </summary>
        /// <returns><c>true</c> if the deployment is locked in (or active) and <c>false</c> otherwise.</returns>
        public bool IsLockedInAtHeight(int height)
        {
            ThresholdState state = this.cache.GetState(this.chainIndexer.GetHeader(height).Previous, this.deployment);
            return state == ThresholdState.LockedIn || state == ThresholdState.Active;
        }
    }
}
