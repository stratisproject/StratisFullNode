using System;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.Tests.Base.Deployments
{
    public class ActivationHeightProviderTest : TestBase
   {
        private ChainIndexer chainIndexer;

        public ActivationHeightProviderTest() : base(new StraxRegTest())
        {
            this.chainIndexer = new ChainIndexer(this.Network);
            this.AppendBlocksToChain(this.chainIndexer, this.CreatePosBlocks(1000));
        }

        [Fact]
        public void CanDetermineActivationHeight()
        {
            var cache = new ThresholdConditionCache(this.Network, this.chainIndexer);

            // Deployment 2 is always active and should return 1.
            var activationHeightProvider2 = new ActivationHeightProvider(this.Network, cache, this.chainIndexer, 2);
            Assert.Equal(1, activationHeightProvider2.ActivationHeight);

            // Set start height to 73 for deployment 1.
            int startHeight = 73;
            int deploymentIndex = 1;
            for (ChainedHeader current = this.chainIndexer.Tip; current != null; current = current.Previous)
            {
                current.Header.Version = (int)(ThresholdConditionCache.VersionbitsTopBits | ((current.Height >= startHeight) ? (1 << deploymentIndex) : 0));
            }

            this.Network.Consensus.BIP9Deployments[deploymentIndex].SetPrivatePropertyValue("StartTime", DateTimeOffset.Parse("1970-1-1"));

            var activationHeightProvider1 = new ActivationHeightProvider(this.Network, cache, this.chainIndexer, deploymentIndex);

            int expectedActivationHeight = ((startHeight / this.Network.Consensus.MinerConfirmationWindow) + 3) * this.Network.Consensus.MinerConfirmationWindow;

            Assert.Equal(expectedActivationHeight, activationHeightProvider1.ActivationHeight);
        }
    }
}
