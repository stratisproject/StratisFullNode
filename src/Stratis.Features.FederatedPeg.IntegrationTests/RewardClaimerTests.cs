using NBitcoin;
using Stratis.Bitcoin.Features.Miner.Interfaces;
using Stratis.Bitcoin.Features.Miner.Staking;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Features.FederatedPeg.IntegrationTests
{
    public sealed class RewardClaimerTests
    {
        private class ConsensusOptionsTest : PosConsensusOptions
        {
            public ConsensusOptionsTest() : base(
                maxBlockBaseSize: 1_000_000,
                maxStandardVersion: 2,
                maxStandardTxWeight: 100_000,
                maxBlockSigopsCost: 20_000,
                maxStandardTxSigopsCost: 20_000 / 5,
                witnessScaleFactor: 4)
            {
                this.RewardClaimerBatchActivationHeight = 30;
            }

            public override int GetStakeMinConfirmations(int height, Network network)
            {
                return 1;
            }
        }

        [Fact]
        public void RewardsToSideChainCanBeBatched()
        {
            using var builder = NodeBuilder.Create(this);

            var configParameters = new NodeConfigParameters { { "txindex", "1" } };
            var network = new StraxRegTest();
            network.Consensus.Options = new ConsensusOptionsTest();

            // Start 2 nodes
            CoreNode nodeA = builder.CreateStratisPosNode(network, "rewards-1-nodeA", configParameters: configParameters).OverrideDateTimeProvider().WithWallet().Start();
            CoreNode nodeB = builder.CreateStratisPosNode(network, "rewards-1-nodeB", configParameters: configParameters).OverrideDateTimeProvider().WithWallet().Start();

            TestHelper.ConnectAndSync(nodeA, nodeB);

            // Generate pre-mine.
            TestHelper.MineBlocks(nodeA, (int)network.Consensus.PremineHeight);

            // Mine blocks to maturity
            TestHelper.MineBlocks(nodeA, (int)network.Consensus.CoinbaseMaturity + 10);

            // Start staking on the node.
            IPosMinting minter = nodeA.FullNode.NodeService<IPosMinting>();
            minter.Stake(new WalletSecret() { WalletName = "mywallet", WalletPassword = "password" });

            // Stake to block height 31
            TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(nodeA, 31));
            TestBase.WaitLoop(() => TestHelper.AreNodesSynced(nodeA, nodeB));

            // Stop staking
            minter.StopStake();

            // Check that only block 30 contains a batched reward transaction to the multisig.
            NBitcoin.ChainedHeader chainedHeader29 = nodeB.FullNode.ChainIndexer.GetHeader(29);
            NBitcoin.Block block19 = nodeB.FullNode.BlockStore().GetBlock(chainedHeader29.HashBlock);

            NBitcoin.ChainedHeader chainedHeader30 = nodeB.FullNode.ChainIndexer.GetHeader(30);
            NBitcoin.Block block20 = nodeB.FullNode.BlockStore().GetBlock(chainedHeader30.HashBlock);
        }
    }
}
