using System;
using System.Collections.Generic;
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
                this.RewardClaimerBatchActivationHeight = 30;
                this.RewardClaimerBlockInterval = 10;

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
            minter.Stake(new List<WalletSecret>() {
                new WalletSecret()
                {
                    WalletName = "mywallet", WalletPassword = "password"
                }
            });

            // Stake to block height 40
            TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(nodeA, 40, 120), waitTimeSeconds: 120);
            TestBase.WaitLoop(() => TestHelper.AreNodesSynced(nodeA, nodeB));

            // Stop staking
            minter.StopStake();

            // Mine 1 more block to include the batched reward transaction in the mempool.
            TestHelper.MineBlocks(nodeA, 1);
            TestBase.WaitLoop(() => TestHelper.AreNodesSynced(nodeA, nodeB));

            // The first staked block would have been 21 and as min confs is 1, the first eligible blocks
            // for rewards would have been 22 and the first tx woul dhave ony been included in block 23.
            // Check that blocks 23 to 30 each contains only 1 reward transaction.
            for (int height = 23; height <= 30; height++)
            {
                // Check that only block 31 contains a batched reward transaction to the multisig.
                ChainedHeader chainedHeader = nodeB.FullNode.ChainIndexer.GetHeader(height);
                Block block = nodeB.FullNode.BlockStore().GetBlock(chainedHeader.HashBlock);
                Assert.Equal(3, block.Transactions.Count);

                Transaction rewardTransaction = block.Transactions[2];
                Assert.Equal(2, rewardTransaction.Outputs.Count);

                Assert.Equal(Money.Coins(9), rewardTransaction.Outputs[1].Value);
                TxOut cirrusDummyAddressSingle = rewardTransaction.Outputs[0];
                Assert.Equal(StraxCoinstakeRule.CirrusTransactionTag(network.CirrusRewardDummyAddress), cirrusDummyAddressSingle.ScriptPubKey);
                TxOut multiSigAddressSingle = rewardTransaction.Outputs[1];
                Assert.Equal(network.Federations.GetOnlyFederation().MultisigScript.PaymentScript, multiSigAddressSingle.ScriptPubKey);
            }

            // Check that blocks 31 to 40 does not contain reward transactions.
            for (int height = 31; height <= 40; height++)
            {
                ChainedHeader chainedHeader = nodeB.FullNode.ChainIndexer.GetHeader(height);
                Block block = nodeB.FullNode.BlockStore().GetBlock(chainedHeader.HashBlock);
                Assert.Equal(2, block.Transactions.Count);
            }

            // Check that only block 41 contains a batched reward transaction to the multisig.
            ChainedHeader chainedHeader41 = nodeB.FullNode.ChainIndexer.GetHeader(41);
            Block block41 = nodeB.FullNode.BlockStore().GetBlock(chainedHeader41.HashBlock);
            Assert.Equal(3, block41.Transactions.Count);
            Assert.Equal(2, block41.Transactions[2].Outputs.Count);

            // The first block to count towards batched rewards would be at height 30, so that will 10 x 9 (90) STRAX.
            Assert.Equal(Money.Coins(90), block41.Transactions[2].Outputs[1].Value);
            TxOut cirrusDummy = block41.Transactions[2].Outputs[0];
            Assert.Equal(StraxCoinstakeRule.CirrusTransactionTag(network.CirrusRewardDummyAddress), cirrusDummy.ScriptPubKey);
            TxOut multiSigAddress = block41.Transactions[2].Outputs[1];
            Assert.Equal(network.Federations.GetOnlyFederation().MultisigScript.PaymentScript, multiSigAddress.ScriptPubKey);
        }
    }
}
