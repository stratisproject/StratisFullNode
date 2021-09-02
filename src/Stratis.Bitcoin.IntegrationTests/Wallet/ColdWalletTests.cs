using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Protocol;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.ColdStaking;
using Stratis.Bitcoin.Features.ColdStaking.Controllers;
using Stratis.Bitcoin.Features.ColdStaking.Models;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.Miner.Interfaces;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Controllers;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.IntegrationTests.Common.ReadyData;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Features.SQLiteWalletRepository;
using Xunit;

namespace Stratis.Bitcoin.IntegrationTests.Wallet
{
    /// <summary>
    /// Contains integration tests for the cold wallet feature.
    /// </summary>
    public class ColdWalletTests
    {
        private const string Password = "password";
        private const string WalletName = "mywallet";
        private const string Account = "account 0";

        /// <summary>
        /// Creates the transaction build context.
        /// </summary>
        /// <param name="network">The network that the context is for.</param>
        /// <param name="accountReference">The wallet account providing the funds.</param>
        /// <param name="password">the wallet password.</param>
        /// <param name="destinationScript">The destination script where we are sending the funds to.</param>
        /// <param name="amount">the amount of money to send.</param>
        /// <param name="feeType">The fee type.</param>
        /// <param name="minConfirmations">The minimum number of confirmations.</param>
        /// <returns>The transaction build context.</returns>
        private static TransactionBuildContext CreateContext(Network network, WalletAccountReference accountReference, string password,
            Script destinationScript, Money amount, FeeType feeType, int minConfirmations)
        {
            return new TransactionBuildContext(network)
            {
                AccountReference = accountReference,
                MinConfirmations = minConfirmations,
                FeeType = feeType,
                WalletPassword = password,
                Recipients = new[] { new Recipient { Amount = amount, ScriptPubKey = destinationScript } }.ToList()
            };
        }

        /// <summary>
        /// Creates a cold staking node.
        /// </summary>
        /// <param name="nodeBuilder">The node builder that will be used to build the node.</param>
        /// <param name="network">The network that the node is being built for.</param>
        /// <param name="dataDir">The data directory used by the node.</param>
        /// <param name="coldStakeNode">Set to <c>false</c> to create a normal (non-cold-staking) node.</param>
        /// <returns>The created cold staking node.</returns>
        private CoreNode CreatePowPosMiningNode(NodeBuilder nodeBuilder, Network network, string dataDir, bool coldStakeNode)
        {
            var extraParams = new NodeConfigParameters { { "datadir", dataDir } };

            var buildAction = new Action<IFullNodeBuilder>(builder =>
            {
                builder.UseBlockStore()
                 .UsePosConsensus()
                 .UseMempool();

                if (coldStakeNode)
                {
                    builder.UseColdStakingWallet();
                }
                else
                {
                    builder.UseWallet();
                }

                builder
                 .AddSQLiteWalletRepository()
                 .AddPowPosMining(true)
                 .AddRPC()
                 .UseApi()
                 .UseTestChainedHeaderTree()
                 .MockIBD();
            });

            return nodeBuilder.CreateCustomNode(buildAction, network, ProtocolVersion.PROVEN_HEADER_VERSION, configParameters: extraParams);
        }

        /// <summary>
        /// Tests whether a cold stake can be minted.
        /// </summary>
        /// <description>
        /// Sends funds from mined by a sending node to the hot wallet node. The hot wallet node creates
        /// the cold staking setup using a cold staking address obtained from the cold wallet node.
        /// Success is determined by whether the balance in the cold wallet increases.
        /// </description>
        [Fact]
        [Trait("Unstable", "True")]
        public async Task WalletCanMineWithColdWalletCoinsAsync()
        {
            using (var builder = NodeBuilder.Create(this))
            {
                var network = new StraxRegTest();

                CoreNode stratisSender = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: false);
                CoreNode stratisHotStake = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: true);
                CoreNode stratisColdStake = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: true);

                stratisSender.WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();
                stratisHotStake.WithWallet().Start();
                stratisColdStake.WithWallet().Start();

                var senderWalletManager = stratisSender.FullNode.WalletManager() as ColdStakingManager;
                var coldWalletManager = stratisColdStake.FullNode.WalletManager() as ColdStakingManager;
                var hotWalletManager = stratisHotStake.FullNode.WalletManager() as ColdStakingManager;

                // Set up cold staking account on cold wallet.
                coldWalletManager.GetOrCreateColdStakingAccount(WalletName, true, Password, null);
                HdAddress coldWalletAddress = coldWalletManager.GetFirstUnusedColdStakingAddress(WalletName, true);

                // Set up cold staking account on hot wallet.
                hotWalletManager.GetOrCreateColdStakingAccount(WalletName, false, Password, null);
                HdAddress hotWalletAddress = hotWalletManager.GetFirstUnusedColdStakingAddress(WalletName, false);

                var walletAccountReference = new WalletAccountReference(WalletName, Account);
                long total2 = stratisSender.FullNode.WalletManager().GetSpendableTransactionsInAccount(walletAccountReference, 1).Sum(s => s.Transaction.Amount);

                // Sync all nodes
                TestHelper.ConnectAndSync(stratisHotStake, stratisSender);
                TestHelper.ConnectAndSync(stratisHotStake, stratisColdStake);
                TestHelper.Connect(stratisSender, stratisColdStake);

                // Send coins to hot wallet.
                Money amountToSend = total2 - network.Consensus.ProofOfWorkReward;
                HdAddress sendto = hotWalletManager.GetUnusedAddress(new WalletAccountReference(WalletName, Account));

                Transaction transaction1 = stratisSender.FullNode.WalletTransactionHandler().BuildTransaction(CreateContext(stratisSender.FullNode.Network, new WalletAccountReference(WalletName, Account), Password, sendto.ScriptPubKey, amountToSend, FeeType.Medium, 1));

                // Broadcast to the other node
                await stratisSender.FullNode.NodeController<WalletController>().SendTransaction(new SendTransactionRequest(transaction1.ToHex()));

                // Wait for the transaction to arrive
                TestBase.WaitLoop(() => stratisHotStake.CreateRPCClient().GetRawMempool().Length > 0);
                Assert.NotNull(stratisHotStake.CreateRPCClient().GetRawTransaction(transaction1.GetHash(), null, false));
                TestBase.WaitLoop(() => stratisHotStake.FullNode.WalletManager().GetSpendableTransactionsInWallet(WalletName).Any());

                long receiveTotal = stratisHotStake.FullNode.WalletManager().GetSpendableTransactionsInWallet(WalletName).Sum(s => s.Transaction.Amount);
                Assert.Equal(amountToSend, (Money)receiveTotal);
                Assert.Null(stratisHotStake.FullNode.WalletManager().GetSpendableTransactionsInWallet(WalletName).First().Transaction.BlockHeight);

                // Setup cold staking from the hot wallet.
                Money amountToSend2 = receiveTotal - network.Consensus.ProofOfWorkReward;
                (Transaction transaction2, _) = hotWalletManager.GetColdStakingSetupTransaction(stratisHotStake.FullNode.WalletTransactionHandler(),
                    coldWalletAddress.Address, hotWalletAddress.Address, WalletName, Account, Password, amountToSend2, new Money(0.02m, MoneyUnit.BTC), false, false, 1, false);

                // Broadcast to the other node
                await stratisHotStake.FullNode.NodeController<WalletController>().SendTransaction(new SendTransactionRequest(transaction2.ToHex()));

                // Wait for the transaction to arrive
                TestBase.WaitLoop(() => coldWalletManager.GetSpendableTransactionsInColdWallet(WalletName, true).Any());

                long receivetotal2 = coldWalletManager.GetSpendableTransactionsInColdWallet(WalletName, true).Sum(s => s.Transaction.Amount);
                Assert.Equal(amountToSend2, (Money)receivetotal2);
                Assert.Null(coldWalletManager.GetSpendableTransactionsInColdWallet(WalletName, true).First().Transaction.BlockHeight);

                // Allow coins to reach maturity
                int stakingMaturity = ((PosConsensusOptions)network.Consensus.Options).GetStakeMinConfirmations(0, network);
                TestHelper.MineBlocks(stratisSender, stakingMaturity, true);

                // Start staking.
                var hotMiningFeature = stratisHotStake.FullNode.NodeFeature<MiningFeature>();
                hotMiningFeature.StartStaking(WalletName, Password);

                TestBase.WaitLoop(() =>
                {
                    var stakingInfo = stratisHotStake.FullNode.NodeService<IPosMinting>().GetGetStakingInfoModel();
                    return stakingInfo.Staking;
                });

                // Wait for money from staking.
                var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(3)).Token;
                TestBase.WaitLoop(() =>
                {
                    // Keep mining to ensure that staking outputs reach maturity.
                    TestHelper.MineBlocks(stratisSender, 1, true);
                    return coldWalletManager.GetSpendableTransactionsInColdWallet(WalletName, true).Sum(s => s.Transaction.Amount) > receivetotal2;
                }, cancellationToken: cancellationToken);
            }
        }

        [Fact]
        [Trait("Unstable", "True")]
        public async Task CanRetrieveFilteredUtxosAsync()
        {
            using (var builder = NodeBuilder.Create(this))
            {
                var network = new StraxRegTest();

                CoreNode stratisSender = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: false);
                CoreNode stratisColdStake = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: true);

                stratisSender.WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();
                stratisColdStake.WithWallet().Start();

                var coldWalletManager = stratisColdStake.FullNode.WalletManager() as ColdStakingManager;

                // Set up cold staking account on cold wallet.
                coldWalletManager.GetOrCreateColdStakingAccount(WalletName, true, Password, null);
                HdAddress coldWalletAddress = coldWalletManager.GetFirstUnusedColdStakingAddress(WalletName, true);

                var walletAccountReference = new WalletAccountReference(WalletName, Account);
                long total2 = stratisSender.FullNode.WalletManager().GetSpendableTransactionsInAccount(walletAccountReference, 1).Sum(s => s.Transaction.Amount);

                // Sync nodes.
                TestHelper.Connect(stratisSender, stratisColdStake);

                // Send coins to cold address.
                Money amountToSend = total2 - network.Consensus.ProofOfWorkReward;
                Transaction transaction1 = stratisSender.FullNode.WalletTransactionHandler().BuildTransaction(CreateContext(stratisSender.FullNode.Network, new WalletAccountReference(WalletName, Account), Password, coldWalletAddress.ScriptPubKey, amountToSend, FeeType.Medium, 1));

                // Broadcast to the other nodes.
                await stratisSender.FullNode.NodeController<WalletController>().SendTransaction(new SendTransactionRequest(transaction1.ToHex()));

                // Wait for the transaction to arrive.
                TestBase.WaitLoop(() => stratisColdStake.CreateRPCClient().GetRawMempool().Length > 0);

                // Despite the funds being sent to an address in the cold account, the wallet does not recognise the output as funds belonging to it.
                Assert.True(stratisColdStake.FullNode.WalletManager().GetBalances(WalletName, Account).Sum(a => a.AmountUnconfirmed + a.AmountUnconfirmed) == 0);

                uint256[] mempoolTransactionId = stratisColdStake.CreateRPCClient().GetRawMempool();

                Transaction misspentTransaction = stratisColdStake.CreateRPCClient().GetRawTransaction(mempoolTransactionId[0]);

                // Now retrieve the UTXO sent to the cold address. The funds will reappear in a normal account on the cold staking node.
                stratisColdStake.FullNode.NodeController<ColdStakingController>().RetrieveFilteredUtxos(new RetrieveFilteredUtxosRequest() { WalletName = stratisColdStake.WalletName, WalletPassword = stratisColdStake.WalletPassword, Hex = misspentTransaction.ToHex(), WalletAccount = null, Broadcast = true});

                TestBase.WaitLoop(() => stratisColdStake.FullNode.WalletManager().GetBalances(WalletName, Account).Sum(a => a.AmountUnconfirmed + a.AmountUnconfirmed) > 0);
            }
        }
    }
}
