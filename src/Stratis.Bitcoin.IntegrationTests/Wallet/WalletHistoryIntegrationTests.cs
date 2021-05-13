using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Stratis.Bitcoin.Features.Miner.Interfaces;
using Stratis.Bitcoin.Features.Miner.Staking;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.IntegrationTests.Wallet
{
    public sealed class WalletHistoryIntegrationTests
    {
        [Fact]
        public async Task WalletCanReturnStakingHistoryCorrectlyAsync()
        {
            using var builder = NodeBuilder.Create(this);

            var configParameters = new NodeConfigParameters { { "txindex", "1" } };
            var network = new StraxRegTestAdjusedCoinbaseMaturity();

            // Start 2 nodes
            CoreNode miner = builder.CreateStratisPosNode(network, "history-1-nodeA", configParameters: configParameters).OverrideDateTimeProvider().WithWallet().Start();
            CoreNode syncer = builder.CreateStratisPosNode(network, "history-1-nodeB", configParameters: configParameters).OverrideDateTimeProvider().WithWallet().Start();

            TestHelper.ConnectAndSync(miner, syncer);

            // Get mining address
            IEnumerable<string> miningAddresses = await $"http://localhost:{miner.ApiPort}/api"
                .AppendPathSegment("wallet/unusedAddresses")
                .SetQueryParams(new { walletName = "mywallet", accountName = "account 0", count = 1 })
                .GetJsonAsync<IEnumerable<string>>();

            // Assert empty history call result.
            var noHistoryCall = $"http://localhost:{miner.ApiPort}/api"
                .AppendPathSegment("wallet/history")
                .SetQueryParams(new WalletHistoryRequest { WalletName = "mywallet", AccountName = "account 0" })
                .GetAsync()
                .ReceiveJson<WalletHistoryModel>().GetAwaiter().GetResult();
            Assert.Empty(noHistoryCall.AccountsHistoryModel.First().TransactionsHistory);

            // Mine some blocks to receive the premine and mature the chain.
            TestHelper.MineBlocks(miner, 20, miningAddress: miningAddresses.First());

            // Start staking on the node.
            IPosMinting minter = miner.FullNode.NodeService<IPosMinting>();
            minter.Stake(new List<WalletSecret>() {
                new WalletSecret()
                {
                    WalletName = "mywallet", WalletPassword = "password"
                }
            });

            // Stake to block height 30
            TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(miner, 30, 120), waitTimeSeconds: 120);

            // Stop staking.
            minter.StopStake();

            // Ensure nodes are synced.
            TestBase.WaitLoop(() => TestHelper.AreNodesSynced(miner, syncer));

            // Assert ordering
            var history = await CallHistoryAsync(miner);

            // Staking items should appear first in the result.
            var stakingTxs = history.Where(t => t.Type == TransactionItemType.Staked);
            Assert.NotEmpty(stakingTxs);
            var index = 0;
            do
            {
                var item = history[index];
                if (item.Type == TransactionItemType.Mined)
                    break;

                Assert.Equal(Money.Coins(9), item.Amount);
                Assert.Equal(TransactionItemType.Staked, item.Type);

                index += 1;
            } while (true);

            // Then the rest are mining txs
            for (int mIndex = index; mIndex < index + 20; mIndex++)
            {
                var item = history[mIndex];
                if ((miner.FullNode.ChainIndexer.Tip.Height - 2) == mIndex)
                    Assert.Equal(Money.Coins(130_000_000), item.Amount); //premine
                else
                    Assert.Equal(Money.Coins(18), item.Amount);

                Assert.Equal(miningAddresses.First(), item.ToAddress);
                Assert.Equal(TransactionItemType.Mined, item.Type);
            }

            // Assert Pagination and ordering
            var paging = $"http://localhost:{miner.ApiPort}/api"
                .AppendPathSegment("wallet/history")
                .SetQueryParams(new WalletHistoryRequest { WalletName = "mywallet", AccountName = "account 0", Skip = 0, Take = 10 })
                .GetAsync()
                .ReceiveJson<WalletHistoryModel>().GetAwaiter().GetResult();

            Assert.Equal(10, paging.AccountsHistoryModel.First().TransactionsHistory.Count());
        }

        [Fact]
        public async Task MiningWalletCanReturnHistoryCorrectlyAsync()
        {
            using (var builder = NodeBuilder.Create(this))
            {
                var network = new StraxRegTestAdjusedCoinbaseMaturity();

                // Start 3 nodes
                CoreNode miner = builder.CreateStratisPosNode(network, "history-1-nodeA").WithWallet().Start();
                CoreNode nodeA = builder.CreateStratisPosNode(network, "history-1-nodeB").WithWallet().Start();
                CoreNode nodeB = builder.CreateStratisPosNode(network, "history-1-nodeC").WithWallet().Start();

                TestHelper.ConnectAndSync(miner, nodeA, nodeB);

                // Get mining address
                IEnumerable<string> miningAddresses = await $"http://localhost:{miner.ApiPort}/api"
                    .AppendPathSegment("wallet/unusedAddresses")
                    .SetQueryParams(new { walletName = "mywallet", accountName = "account 0", count = 1 })
                    .GetJsonAsync<IEnumerable<string>>();

                // Assert empty history call result.
                var noHistoryCall = $"http://localhost:{miner.ApiPort}/api"
                    .AppendPathSegment("wallet/history")
                    .SetQueryParams(new WalletHistoryRequest { WalletName = "mywallet", AccountName = "account 0" })
                    .GetAsync()
                    .ReceiveJson<WalletHistoryModel>().GetAwaiter().GetResult();
                Assert.Empty(noHistoryCall.AccountsHistoryModel.First().TransactionsHistory);

                // Mine some blocks to receive the premine.
                TestHelper.MineBlocks(miner, 5, miningAddress: miningAddresses.First());

                // Send some coins to nodeA
                TestHelper.SendCoins(miner, miner, new[] { nodeA }, Money.Coins(1000));

                // Call the history method on node A
                var history = await CallHistoryAsync(nodeA);
                Assert.Single(history);

                // Mine the coins and advance the chain.
                TestHelper.MineBlocks(miner, 4);

                // Send some more coins to nodeA
                TestHelper.SendCoins(miner, miner, new[] { nodeA }, Money.Coins(2000));

                // Call the history method on node A
                history = await CallHistoryAsync(nodeA);
                Assert.Equal(2, history.Count);

                // Mine the coins and advance the chain.
                TestHelper.MineBlocks(miner, 4);

                // Send coins from NodeA to NodeB (spend some of the first utxo)
                var coins = nodeA.FullNode.WalletManager().GetSpendableTransactionsInWallet("mywallet");
                TestHelper.SendCoins(miner, nodeA, new[] { nodeB }, Money.Coins(900), coins.Where(c => c.Transaction.Amount <= Money.Coins(1000)).Select(c => c.ToOutPoint()).ToList());

                // Ensure nodes are synced.
                TestBase.WaitLoop(() => TestHelper.AreNodesSynced(miner, nodeB));

                // Call the history method on node A
                history = await CallHistoryAsync(nodeA);
                Assert.Equal(3, history.Count);

                Assert.Equal(TransactionItemType.Send, history[0].Type);
                Assert.Equal(TransactionItemType.Received, history[1].Type);
                Assert.Equal(TransactionItemType.Received, history[2].Type);

                // Assert payment details and change on sent tx.
                var singleTxCall = $"http://localhost:{nodeA.ApiPort}/api"
                    .AppendPathSegment("wallet/history")
                    .SetQueryParams(new WalletHistoryRequest { WalletName = "mywallet", AccountName = "account 0", SearchQuery = history[0].Id.ToString() })
                    .GetAsync()
                    .ReceiveJson<WalletHistoryModel>().GetAwaiter().GetResult();

                var sendTx = singleTxCall.AccountsHistoryModel.First().TransactionsHistory.First();
                Assert.Equal(2, sendTx.Payments.Count);
                var changePayments = sendTx.Payments.Where(p => p.IsChange);
                var normalPayments = sendTx.Payments.Where(p => !p.IsChange);
                Assert.Single(changePayments);
                Assert.Single(normalPayments);
                Assert.Equal(Money.Coins(900), normalPayments.First().Amount);
            }
        }

        [Fact]
        public async Task WalletCanReturnHistoryCorrectlyToMultipleRecipientsAsync()
        {
            using (var builder = NodeBuilder.Create(this))
            {
                var network = new StraxRegTestAdjusedCoinbaseMaturity();

                // Start 4 nodes
                CoreNode miner = builder.CreateStratisPosNode(network, "history-3-nodeA").WithWallet().Start();
                CoreNode nodeA = builder.CreateStratisPosNode(network, "history-3-nodeB").WithWallet().Start();
                CoreNode nodeB = builder.CreateStratisPosNode(network, "history-3-nodeC").WithWallet().Start();
                CoreNode nodeC = builder.CreateStratisPosNode(network, "history-3-nodeD").WithWallet().Start();

                TestHelper.ConnectAndSync(miner, nodeA, nodeB, nodeC);

                // Mine some blocks to receive the premine.
                TestHelper.MineBlocks(miner, 10);

                // Send some coins to nodeA, nodeB and nodeC
                TestHelper.SendCoins(miner, miner, new CoreNode[] { nodeA, nodeB, nodeC }, Money.Coins(100));

                // Single receive tx
                var history = await CallHistoryAsync(nodeA);
                Assert.Single(history);

                // Single receive tx
                history = await CallHistoryAsync(nodeB);
                Assert.Single(history);

                // Single receive tx
                history = await CallHistoryAsync(nodeC);
                Assert.Single(history);

                // 3 sends of 100
                history = await CallHistoryAsync(miner);
                var sends = history.Where(t => t.Type == TransactionItemType.Send).ToList();
                Assert.Equal(3, sends.Count());
                Assert.Equal(Money.Coins(100) + sends[0].Fee, sends[0].Amount);
                Assert.Equal(Money.Coins(100) + sends[1].Fee, sends[1].Amount);
                Assert.Equal(Money.Coins(100) + sends[2].Fee, sends[2].Amount);
            }
        }

        private async Task<List<TransactionItemModel>> CallHistoryAsync(CoreNode node)
        {
            WalletHistoryModel historyNodeA = await $"http://localhost:{node.ApiPort}/api"
                                .AppendPathSegment("wallet/history")
                                .SetQueryParams(new WalletHistoryRequest { WalletName = "mywallet", AccountName = "account 0" })
                                .GetAsync()
                                .ReceiveJson<WalletHistoryModel>();

            return historyNodeA.AccountsHistoryModel.First().TransactionsHistory.ToList();
        }

        private class StraxRegTestAdjusedCoinbaseMaturity : StraxRegTest
        {
            public StraxRegTestAdjusedCoinbaseMaturity()
            {
                this.Name = Guid.NewGuid().ToString("N").Substring(0, 7);
                this.RewardClaimerBatchActivationHeight = 30;
                this.RewardClaimerBlockInterval = 10;

                typeof(NBitcoin.Consensus).GetProperty("CoinbaseMaturity").SetValue(this.Consensus, 1);
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
    }
}
