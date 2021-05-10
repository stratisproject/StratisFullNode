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
        public async Task WalletCanReturnHistoryCorrectlyAsync()
        {
            using var builder = NodeBuilder.Create(this);

            var configParameters = new NodeConfigParameters { { "txindex", "1" } };
            var network = new StraxRegTestAdjusedCoinbaseMaturity();

            // Start 2 nodes
            CoreNode nodeA = builder.CreateStratisPosNode(network, "history-1-nodeA", configParameters: configParameters).AddRewardClaimer().OverrideDateTimeProvider().WithWallet().Start();
            CoreNode nodeB = builder.CreateStratisPosNode(network, "history-1-nodeB", configParameters: configParameters).OverrideDateTimeProvider().WithWallet().Start();

            TestHelper.Connect(nodeA, nodeB);

            // Get mining address
            IEnumerable<string> miningAddresses = await $"http://localhost:{nodeA.ApiPort}/api"
                .AppendPathSegment("wallet/unusedAddresses")
                .SetQueryParams(new { walletName = "mywallet", accountName = "account 0", count = 1 })
                .GetJsonAsync<IEnumerable<string>>();

            // Assert empty history call result.
            var noHistoryCall = $"http://localhost:{nodeA.ApiPort}/api"
                .AppendPathSegment("wallet/history")
                .SetQueryParams(new WalletHistoryRequest { WalletName = "mywallet", AccountName = "account 0" })
                .GetAsync()
                .ReceiveJson<WalletHistoryModel>().GetAwaiter().GetResult();
            Assert.Empty(noHistoryCall.AccountsHistoryModel.First().TransactionsHistory);

            // Mine some blocks to receive the premine.
            TestHelper.MineBlocks(nodeA, 5, miningAddress: miningAddresses.First());

            // Send some coins to nodeB
            TestHelper.SendCoins(nodeA, nodeB, Money.Coins(1000));

            // Mine the coins and advance the chain.
            TestHelper.MineBlocks(nodeA, 15);

            // Start staking on the node.
            IPosMinting minter = nodeA.FullNode.NodeService<IPosMinting>();
            minter.Stake(new List<WalletSecret>() {
                new WalletSecret()
                {
                    WalletName = "mywallet", WalletPassword = "password"
                }
            });

            // Stake to block height 30
            TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(nodeA, 30, 120), waitTimeSeconds: 120);

            // Stop staking.
            minter.StopStake();

            // Ensure nodes are synced.
            TestBase.WaitLoop(() => TestHelper.AreNodesSynced(nodeA, nodeB));

            // Call the history method on node A
            WalletHistoryModel historyNodeA = $"http://localhost:{nodeA.ApiPort}/api"
                                .AppendPathSegment("wallet/history")
                                .SetQueryParams(new WalletHistoryRequest { WalletName = "mywallet", AccountName = "account 0" })
                                .GetAsync()
                                .ReceiveJson<WalletHistoryModel>().GetAwaiter().GetResult();

            // Assert ordering
            var history = historyNodeA.AccountsHistoryModel.First().TransactionsHistory.ToList();

            // Staking items should appear first in the result.
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

            // Then 15 + 1 mining txs (the 1 is for mining the send tx)
            for (int mIndex = index; mIndex < index + 16; mIndex++)
            {
                var item = history[mIndex];
                Assert.Equal(Money.Coins(18), item.Amount);
                Assert.Equal(miningAddresses.First(), item.ToAddress);
                Assert.Equal(TransactionItemType.Mined, item.Type);
            }

            // Then a send
            var send = history[index + 16 + 1];
            Assert.Equal(Money.Coins(1000) + send.Fee, send.Amount);
            Assert.Equal(TransactionItemType.Send, send.Type);

            // Then 5 mining txs
            for (int mIndex = index + 17; mIndex < mIndex + 17 + 5; mIndex++)
            {
                var item = history[mIndex];
                Assert.Equal(Money.Coins(18), item.Amount);
                Assert.Equal(miningAddresses.First(), item.ToAddress);
                Assert.Equal(TransactionItemType.Mined, item.Type);
            }

            // Assert mining transactions
            //var miningTxs = historyNodeA.AccountsHistoryModel.First().TransactionsHistory.Where(t => t.Type == TransactionItemType.Mined);
            //Assert.Equal(21, miningTxs.Count());
            //var miningTx = miningTxs.First();
            //Assert.Equal(Money.Coins(18), miningTx.Amount);
            //Assert.Equal(miningAddresses.First(), miningTx.ToAddress);
            //Assert.Equal(TransactionItemType.Mined, miningTx.Type);

            //// Assert staking transactions
            //var stakingTxs = historyNodeA.AccountsHistoryModel.First().TransactionsHistory.Where(t => t.Type == TransactionItemType.Staked);
            //Assert.True(stakingTxs.Count() >= 9); // This is because StopStake could on occasion not stop the staking process quick enough.
            //var stakingTx = stakingTxs.First();
            //Assert.Equal(Money.Coins(9), stakingTx.Amount);
            //Assert.Equal(TransactionItemType.Staked, stakingTx.Type);

            //// Assert send transaction
            //var sendTxs = historyNodeA.AccountsHistoryModel.First().TransactionsHistory.Where(t => t.Type == TransactionItemType.Send);
            //Assert.Single(sendTxs);
            //var sendTx = sendTxs.First();
            //Assert.Equal(Money.Coins(1000) + sendTx.Fee, sendTx.Amount);
            //Assert.Equal(TransactionItemType.Send, sendTx.Type);

            // Assert payment details and change on sent tx.
            var sendTxs = historyNodeA.AccountsHistoryModel.First().TransactionsHistory.Where(t => t.Type == TransactionItemType.Send);
            var sendTx = sendTxs.First();
            var singleTxCall = $"http://localhost:{nodeA.ApiPort}/api"
                .AppendPathSegment("wallet/history")
                .SetQueryParams(new WalletHistoryRequest { WalletName = "mywallet", AccountName = "account 0", SearchQuery = sendTx.Id.ToString() })
                .GetAsync()
                .ReceiveJson<WalletHistoryModel>().GetAwaiter().GetResult();
            Assert.Equal(2, sendTx.Payments.Count);
            var changePayments = sendTx.Payments.Where(p => p.IsChange);
            var normalPayments = sendTx.Payments.Where(p => !p.IsChange);
            Assert.Single(changePayments);
            Assert.Single(normalPayments);
            Assert.Equal(Money.Coins(1000), normalPayments.First().Amount);

            // Assert Pagination and ordering
            var paging = $"http://localhost:{nodeA.ApiPort}/api"
                .AppendPathSegment("wallet/history")
                .SetQueryParams(new WalletHistoryRequest { WalletName = "mywallet", AccountName = "account 0", Skip = 0, Take = 10 })
                .GetAsync()
                .ReceiveJson<WalletHistoryModel>().GetAwaiter().GetResult();
            Assert.Equal(10, historyNodeA.AccountsHistoryModel.First().TransactionsHistory.Count());

            // There should be no sends in this set
            var pagedSends = paging.AccountsHistoryModel.First().TransactionsHistory.Where(t => t.Type == TransactionItemType.Send);
            Assert.Empty(pagedSends);

            // Call the history method on node B
            WalletHistoryModel historyNodeB = $"http://localhost:{nodeB.ApiPort}/api"
                                .AppendPathSegment("wallet/history")
                                .SetQueryParams(new { walletName = "mywallet", AccountName = "account 0" })
                                .GetAsync()
                                .ReceiveJson<WalletHistoryModel>().GetAwaiter().GetResult();

            // Assert receiving transaction
            var receiveTxs = historyNodeB.AccountsHistoryModel.First().TransactionsHistory.Where(t => t.Type == TransactionItemType.Received);
            Assert.Single(receiveTxs);
            var receiveTx = receiveTxs.First();
            Assert.Equal(Money.Coins(1000), receiveTx.Amount);
            Assert.Equal(TransactionItemType.Received, receiveTx.Type);
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
