using System;
using NBitcoin;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
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
        private class StraxRegTestAdjusedCoinbaseMaturity : StraxRegTest
        {
            public StraxRegTestAdjusedCoinbaseMaturity()
            {
                this.Name = Guid.NewGuid().ToString("N").Substring(0, 7);

                typeof(Consensus).GetProperty("CoinbaseMaturity").SetValue(this.Consensus, 1);
                this.Consensus.Options = new ConsensusOptionsAdjustedMinStakeConfirmations();
            }
        }

        private class ConsensusOptionsAdjustedMinStakeConfirmations : PosConsensusOptions
        {
            public ConsensusOptionsAdjustedMinStakeConfirmations() : base(
                maxBlockBaseSize: 1_000_000,
                maxStandardVersion: 2,
                maxStandardTxWeight: 100_000,
                maxBlockSigopsCost: 20_000,
                maxStandardTxSigopsCost: 20_000 / 5,
                witnessScaleFactor: 4)
            {
                this.RewardClaimerBatchActivationHeight = 20;
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
            var network = new StraxRegTestAdjusedCoinbaseMaturity();

            // Start 2 nodes
            CoreNode nodeA = builder.CreateStratisPosNode(network, "rewards-1-nodeA", configParameters: configParameters).AddRewardClaimer().OverrideDateTimeProvider().WithWallet().Start();
            CoreNode nodeB = builder.CreateStratisPosNode(network, "rewards-1-nodeB", configParameters: configParameters).OverrideDateTimeProvider().WithWallet().Start();

            TestHelper.ConnectAndSync(nodeA, nodeB);

            // Mine some blocks to mature the premine.
            TestHelper.MineBlocks(nodeA, 20);

            // Start staking on the node.
            IPosMinting minter = nodeA.FullNode.NodeService<IPosMinting>();
            minter.Stake(new WalletSecret() { WalletName = "mywallet", WalletPassword = "password" });

            // Stake to block height 30
            TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(nodeA, 30));
            TestBase.WaitLoop(() => TestHelper.AreNodesSynced(nodeA, nodeB));

            // Stop staking
            minter.StopStake();

            // Mine 1 more block to include the reward transaction in the mempool.
            TestHelper.MineBlocks(nodeA, 1);
            TestBase.WaitLoop(() => TestHelper.AreNodesSynced(nodeA, nodeB));

            // Check that block 30 does not contain a reward transaction.
            ChainedHeader chainedHeader30 = nodeB.FullNode.ChainIndexer.GetHeader(30);
            Block block30 = nodeB.FullNode.BlockStore().GetBlock(chainedHeader30.HashBlock);
            Assert.Equal(2, block30.Transactions.Count);

            // Check that only block 31 contains a batched reward transaction to the multisig.
            ChainedHeader chainedHeader31 = nodeB.FullNode.ChainIndexer.GetHeader(31);
            Block block31 = nodeB.FullNode.BlockStore().GetBlock(chainedHeader31.HashBlock);
            Assert.Equal(3, block31.Transactions.Count);
            Assert.Equal(2, block31.Transactions[2].Outputs.Count);

            // The first staked block would have been at height 21 and as min confs is 1,the first block to count towards
            // rewards would be at height 22, so that will 8 x 9 (72) STRAX.
            Assert.Equal(Money.Coins(72), block31.Transactions[2].Outputs[1].Value);
            TxOut cirrusDummy = block31.Transactions[2].Outputs[0];
            Assert.Equal(StraxCoinstakeRule.CirrusTransactionTag(network.CirrusRewardDummyAddress), cirrusDummy.ScriptPubKey);
            TxOut multiSigAddress = block31.Transactions[2].Outputs[1];
            Assert.Equal(network.Federations.GetOnlyFederation().MultisigScript.PaymentScript, multiSigAddress.ScriptPubKey);
        }
    }
}
