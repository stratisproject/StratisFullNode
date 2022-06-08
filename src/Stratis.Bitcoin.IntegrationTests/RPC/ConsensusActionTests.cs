using NBitcoin;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Interfaces;
using Xunit;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Tests.Common;
using Microsoft.AspNetCore.Mvc;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.IntegrationTests.RPC
{
    public class ConsensusActionTests : BaseRPCControllerTest
    {
        [Fact]
        public void CanCall_GetBestBlockHash()
        {
            string dir = CreateTestDir(this);

            IFullNode fullNode = this.BuildServicedNode(dir);
            var controller = fullNode.NodeController<ConsensusController>();

            uint256 result = controller.GetBestBlockHashRPC();

            Assert.Null(result);
        }

        [Fact]
        public void CanCall_GetBlockHash()
        {
            string dir = CreateTestDir(this);

            IFullNode fullNode = this.BuildServicedNode(dir);
            var controller = fullNode.NodeController<ConsensusController>();

            uint256 result = controller.GetBlockHashRPC(0);

            Assert.Null(result);
        }

        [Fact]
        public void CanCall_IsInitialBlockDownload()
        {
            string dir = CreateTestDir(this);

            IFullNode fullNode = this.BuildServicedNode(dir, KnownNetworks.StraxMain);
            var isIBDProvider = fullNode.NodeService<IInitialBlockDownloadState>(true);
            var chainState = fullNode.NodeService<IChainState>(true);
            chainState.ConsensusTip = new ChainedHeader(fullNode.Network.GetGenesis().Header, fullNode.Network.GenesisHash, 0);

            Assert.NotNull(isIBDProvider);
            Assert.True(isIBDProvider.IsInitialBlockDownload());
        }

        [Fact]
        public void CanCall_CommonBlock()
        {
            string dir = CreateTestDir(this);

            IFullNode fullNode = this.BuildServicedNode(dir);
            var controller = fullNode.NodeController<ConsensusController>();

            IActionResult result = controller.CommonBlock(new[] { uint256.Zero, fullNode.Network.GenesisHash, uint256.One });

            Assert.NotNull(result);

            var jsonResult = Assert.IsType<JsonResult>(result);

            var hashHeightPair = Assert.IsType<HashHeightPair>(jsonResult.Value);

            Assert.Equal(hashHeightPair.Height, 0);
            Assert.Equal(hashHeightPair.Hash, fullNode.Network.GenesisHash);
        }
    }
}
