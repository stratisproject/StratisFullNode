using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DBreeze.Utils;
using NBitcoin;
using Stratis.Bitcoin.Features.PoA.IntegrationTests.Common;
using Stratis.Bitcoin.Features.PoA.Voting;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Features.Wallet.Controllers;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Models;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.PoA.Features.Voting;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.Features.PoA.IntegrationTests
{
    public class VotingAndMiningTests : IDisposable
    {
        private readonly TestPoANetwork poaNetwork;

        private readonly PoANodeBuilder builder;

        private readonly CoreNode node1, node2, node3;

        private readonly PubKey testPubKey;

        public VotingAndMiningTests()
        {
            this.testPubKey = new Mnemonic("lava frown leave virtual wedding ghost sibling able liar wide wisdom mammal").DeriveExtKey().PrivateKey.PubKey;
            this.poaNetwork = new TestPoANetwork();

            this.builder = PoANodeBuilder.CreatePoANodeBuilder(this);

            this.node1 = this.builder.CreatePoANode(this.poaNetwork, this.poaNetwork.FederationKey1).Start();
            this.node2 = this.builder.CreatePoANode(this.poaNetwork, this.poaNetwork.FederationKey2).Start();
            this.node3 = this.builder.CreatePoANode(this.poaNetwork, this.poaNetwork.FederationKey3).Start();
        }

        [Fact]
        // Checks that fed members cant vote twice.
        // Checks that miner adds voting data if it exists.
        public async Task CantVoteTwiceAsync()
        {
            int originalFedMembersCount = this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers().Count;

            TestHelper.Connect(this.node1, this.node2);

            await this.node1.MineBlocksAsync(3);

            IFederationMember federationMember = new FederationMember(new PubKey("03025fcadedd28b12665de0542c8096f4cd5af8e01791a4d057f67e2866ca66ba7"));
            byte[] fedMemberBytes = (this.poaNetwork.Consensus.ConsensusFactory as PoAConsensusFactory).SerializeFederationMember(federationMember);
            var votingData = new VotingData()
            {
                Key = VoteKey.AddFederationMember,
                Data = fedMemberBytes
            };

            this.node1.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);

            Assert.Single(this.node1.FullNode.NodeService<VotingManager>().GetScheduledVotes());
            Assert.Empty(this.node1.FullNode.NodeService<VotingManager>().GetPendingPolls());

            await this.node1.MineBlocksAsync(1);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            Assert.Empty(this.node1.FullNode.NodeService<VotingManager>().GetScheduledVotes());
            Assert.Single(this.node1.FullNode.NodeService<VotingManager>().GetPendingPolls());

            // Vote 2nd time and make sure nothing changed.
            this.node1.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);
            await this.node1.MineBlocksAsync(1);
            Assert.Empty(this.node1.FullNode.NodeService<VotingManager>().GetScheduledVotes());
            Assert.Single(this.node1.FullNode.NodeService<VotingManager>().GetPendingPolls());

            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            // Node 2 votes. After that it will be enough to change the federation.
            this.node2.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);

            await this.node2.MineBlocksAsync((int)this.poaNetwork.Consensus.MaxReorgLength + 1);

            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            Assert.Equal(originalFedMembersCount + 1, this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers().Count);
            Assert.Equal(originalFedMembersCount + 1, this.node2.FullNode.NodeService<IFederationManager>().GetFederationMembers().Count);

            TestHelper.Connect(this.node2, this.node3);

            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2, this.node3);
        }

        [Fact]
        // Checks that node can sync from scratch if federation voted in favor of adding a new fed member.
        public async Task CanSyncIfFedMemberAddedAsync()
        {
            List<IFederationMember> originalFedMembers = this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers();

            TestHelper.Connect(this.node1, this.node2);

            IFederationMember federationMember = new FederationMember(new PubKey("03025fcadedd28b12665de0542c8096f4cd5af8e01791a4d057f67e2866ca66ba7"));
            byte[] fedMemberBytes = (this.poaNetwork.Consensus.ConsensusFactory as PoAConsensusFactory).SerializeFederationMember(federationMember);
            var votingData = new VotingData()
            {
                Key = VoteKey.AddFederationMember,
                Data = fedMemberBytes
            };

            this.node1.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);
            this.node2.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);

            await this.node1.MineBlocksAsync(1);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            await this.node2.MineBlocksAsync((int)this.poaNetwork.Consensus.MaxReorgLength * 3);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            List<IFederationMember> newFedMembers = this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers();

            Assert.Equal(originalFedMembers.Count + 1, newFedMembers.Count);

            TestHelper.Connect(this.node2, this.node3);

            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2, this.node3);
        }

        // TODO : Rewrite.
        //[Fact]
        // Checks that multisig fed members can't be kicked.
        //public async Task CantKickMultiSigFedMemberAsync()
        //{
        //    var network = new TestPoACollateralNetwork();
        //    CoreNode node = this.builder.CreatePoANode(network, network.FederationKey1).Start();

        //    var model = new HexPubKeyModel() { PubKeyHex = network.FederationKey2.PubKey.ToHex() };
        //    IActionResult response = node.FullNode.NodeController<FederationVotingController>().VoteKickFedMember(model);
        //    Assert.True(response is ErrorResult errorResult && errorResult.Value is ErrorResponse errorResponse && errorResponse.Errors.First().Message == "Multisig members can't be voted on");
        //}

        [Fact]
        // Checks that node can sync from scratch if federation voted in favor of kicking a fed member.
        public async Task CanSyncIfFedMemberKickedAsync()
        {
            int originalFedMembersCount = this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers().Count;

            TestHelper.Connect(this.node1, this.node2);

            byte[] fedMemberBytes = (this.poaNetwork.Consensus.ConsensusFactory as PoAConsensusFactory).SerializeFederationMember(new FederationMember(this.poaNetwork.FederationKey2.PubKey));
            var votingData = new VotingData()
            {
                Key = VoteKey.KickFederationMember,
                Data = fedMemberBytes
            };

            this.node1.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);
            this.node2.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);

            await this.node2.MineBlocksAsync(1);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            await this.node1.MineBlocksAsync((int)this.poaNetwork.Consensus.MaxReorgLength * 3);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            Assert.Equal(originalFedMembersCount - 1, this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers().Count);

            TestHelper.Connect(this.node2, this.node3);

            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2, this.node3);
        }

        [Fact]
        public async Task CanAddAndRemoveSameFedMemberAsync()
        {
            int originalFedMembersCount = this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers().Count;

            TestHelper.Connect(this.node1, this.node2);
            TestHelper.Connect(this.node2, this.node3);

            await this.AllVoteAndMineAsync(this.testPubKey, true);

            Assert.Equal(originalFedMembersCount + 1, this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers().Count);

            await this.AllVoteAndMineAsync(this.testPubKey, false);

            Assert.Equal(originalFedMembersCount, this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers().Count);

            await this.AllVoteAndMineAsync(this.testPubKey, true);

            Assert.Equal(originalFedMembersCount + 1, this.node1.FullNode.NodeService<IFederationManager>().GetFederationMembers().Count);
        }

        [Fact]
        public async Task ReorgRevertsAppliedChangesAsync()
        {
            TestHelper.Connect(this.node1, this.node2);

            byte[] fedMemberBytes = (this.poaNetwork.Consensus.ConsensusFactory as PoAConsensusFactory).SerializeFederationMember(new FederationMember(this.testPubKey));
            var votingData = new VotingData() { Key = VoteKey.AddFederationMember, Data = fedMemberBytes };
            this.node1.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);

            votingData = new VotingData() { Key = VoteKey.KickFederationMember, Data = fedMemberBytes };
            this.node1.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);
            await this.node1.MineBlocksAsync(1);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            votingData = new VotingData() { Key = VoteKey.AddFederationMember, Data = fedMemberBytes };
            this.node2.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);
            await this.node2.MineBlocksAsync(1);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            Assert.Single(this.node2.FullNode.NodeService<VotingManager>().GetPendingPolls());
            Assert.Single(this.node2.FullNode.NodeService<VotingManager>().GetApprovedPolls());

            await this.node3.MineBlocksAsync(4);
            TestHelper.Connect(this.node2, this.node3);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2, this.node3);

            Assert.Empty(this.node2.FullNode.NodeService<VotingManager>().GetPendingPolls());
            Assert.Empty(this.node2.FullNode.NodeService<VotingManager>().GetApprovedPolls());
        }

        private async Task AllVoteAndMineAsync(PubKey key, bool add)
        {
            await this.VoteAndMineBlockAsync(key, add, this.node1);
            await this.VoteAndMineBlockAsync(key, add, this.node2);
            await this.VoteAndMineBlockAsync(key, add, this.node3);

            await this.node1.MineBlocksAsync((int)this.poaNetwork.Consensus.MaxReorgLength + 1);
        }

        private async Task VoteAndMineBlockAsync(PubKey key, bool add, CoreNode node)
        {
            byte[] fedMemberBytes = (this.poaNetwork.Consensus.ConsensusFactory as PoAConsensusFactory).SerializeFederationMember(new FederationMember(key));
            var votingData = new VotingData()
            {
                Key = add ? VoteKey.AddFederationMember : VoteKey.KickFederationMember,
                Data = fedMemberBytes
            };

            node.FullNode.NodeService<VotingManager>().ScheduleVote(votingData);

            await node.MineBlocksAsync(1);

            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2, this.node3);
        }

        [Fact]
        public async Task CanVoteToWhitelistAndRemoveHashesAsync()
        {
            int maxReorg = (int)this.poaNetwork.Consensus.MaxReorgLength;

            Assert.Empty(this.node1.FullNode.NodeService<IWhitelistedHashesRepository>().GetHashes());
            TestHelper.Connect(this.node1, this.node2);

            await this.node1.MineBlocksAsync(1);

            var model = new HashModel() { Hash = NBitcoin.Crypto.Hashes.Hash256(RandomUtils.GetUInt64().ToBytes()).ToString() };

            // Node 1 votes to add hash
            this.node1.FullNode.NodeController<DefaultVotingController>().VoteWhitelistHash(model);
            await this.node1.MineBlocksAsync(1);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            // Node 2 votes to add hash
            this.node2.FullNode.NodeController<DefaultVotingController>().VoteWhitelistHash(model);
            await this.node2.MineBlocksAsync(maxReorg + 2);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            Assert.Single(this.node1.FullNode.NodeService<IWhitelistedHashesRepository>().GetHashes());

            // Node 1 votes to remove hash
            this.node1.FullNode.NodeController<DefaultVotingController>().VoteRemoveHash(model);
            await this.node1.MineBlocksAsync(1);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            // Node 2 votes to remove hash
            this.node2.FullNode.NodeController<DefaultVotingController>().VoteRemoveHash(model);
            await this.node2.MineBlocksAsync(maxReorg + 2);
            CoreNodePoAExtensions.WaitTillSynced(this.node1, this.node2);

            Assert.Empty(this.node1.FullNode.NodeService<IWhitelistedHashesRepository>().GetHashes());
        }

        [Fact]
        public void NodeCanLoadFederationKey()
        {
            var network = new TestPoANetwork();

            using (PoANodeBuilder builder = PoANodeBuilder.CreatePoANodeBuilder(this))
            {
                // Create first node as fed member.
                Key key = network.FederationKey1;
                CoreNode node = builder.CreatePoANode(network, key).Start();

                Assert.True(node.FullNode.NodeService<IFederationManager>().IsFederationMember);
                Assert.Equal(node.FullNode.NodeService<IFederationManager>().CurrentFederationKey, key);

                // Create second node as normal node.
                CoreNode node2 = builder.CreatePoANode(network).Start();

                Assert.False(node2.FullNode.NodeService<IFederationManager>().IsFederationMember);
                Assert.Null(node2.FullNode.NodeService<IFederationManager>().CurrentFederationKey);
            }
        }

        [Fact]
        public async Task NodeCanMineAsync()
        {
            var network = new TestPoANetwork();

            using (PoANodeBuilder builder = PoANodeBuilder.CreatePoANodeBuilder(this))
            {
                CoreNode node = builder.CreatePoANode(network, network.FederationKey1).Start();

                int tipBefore = node.GetTip().Height;

                await node.MineBlocksAsync(5).ConfigureAwait(false);

                Assert.True(node.GetTip().Height >= tipBefore + 5);
            }
        }

        [Fact]
        public async Task PremineIsReceivedAsync()
        {
            TestPoANetwork network = new TestPoANetwork();

            using (PoANodeBuilder builder = PoANodeBuilder.CreatePoANodeBuilder(this))
            {
                string walletName = "mywallet";
                CoreNode node = builder.CreatePoANode(network, network.FederationKey1).WithWallet("pass", walletName).Start();

                IWalletManager walletManager = node.FullNode.NodeService<IWalletManager>();
                long balanceOnStart = walletManager.GetBalances(walletName, "account 0").Sum(x => x.AmountConfirmed);
                Assert.Equal(0, balanceOnStart);

                long toMineCount = network.Consensus.PremineHeight + network.Consensus.CoinbaseMaturity + 1 - node.GetTip().Height;

                await node.MineBlocksAsync((int)toMineCount).ConfigureAwait(false);

                TestBase.WaitLoop(() =>
                {
                    long balanceAfterPremine = walletManager.GetBalances(walletName, "account 0").Sum(x => x.AmountConfirmed);

                    return network.Consensus.PremineReward.Satoshi == balanceAfterPremine;
                });
            }
        }

        [Fact]
        public async Task TransactionSentFeesReceivedByMinerAsync()
        {
            TestPoANetwork network = new TestPoANetwork();

            using (PoANodeBuilder builder = PoANodeBuilder.CreatePoANodeBuilder(this))
            {
                string walletName = "mywallet";
                string walletPassword = "pass";
                string walletAccount = "account 0";

                Money transferAmount = Money.Coins(1m);
                Money feeAmount = Money.Coins(0.0001m);

                CoreNode nodeA = builder.CreatePoANode(network, network.FederationKey1).WithWallet(walletPassword, walletName).Start();
                CoreNode nodeB = builder.CreatePoANode(network, network.FederationKey2).WithWallet(walletPassword, walletName).Start();

                TestHelper.Connect(nodeA, nodeB);

                long toMineCount = network.Consensus.PremineHeight + network.Consensus.CoinbaseMaturity + 1 - nodeA.GetTip().Height;

                // Get coins on nodeA via the premine.
                await nodeA.MineBlocksAsync((int)toMineCount).ConfigureAwait(false);

                CoreNodePoAExtensions.WaitTillSynced(nodeA, nodeB);

                // Will send funds to one of nodeB's addresses.
                Script destination = nodeB.FullNode.WalletManager().GetUnusedAddress().ScriptPubKey;

                var context = new TransactionBuildContext(network)
                {
                    AccountReference = new WalletAccountReference(walletName, walletAccount),
                    MinConfirmations = 0,
                    FeeType = FeeType.High,
                    WalletPassword = walletPassword,
                    Recipients = new[] { new Recipient { Amount = transferAmount, ScriptPubKey = destination } }.ToList()
                };

                Transaction trx = nodeA.FullNode.WalletTransactionHandler().BuildTransaction(context);

                Assert.True(context.TransactionBuilder.Verify(trx, out _));

                await nodeA.FullNode.NodeController<WalletController>().SendTransaction(new SendTransactionRequest(trx.ToHex()));

                TestBase.WaitLoop(() => nodeA.CreateRPCClient().GetRawMempool().Length == 1 && nodeB.CreateRPCClient().GetRawMempool().Length == 1);

                await nodeB.MineBlocksAsync((int)toMineCount).ConfigureAwait(false);

                TestBase.WaitLoop(() => nodeA.CreateRPCClient().GetRawMempool().Length == 0 && nodeB.CreateRPCClient().GetRawMempool().Length == 0);

                IWalletManager walletManager = nodeB.FullNode.NodeService<IWalletManager>();

                TestBase.WaitLoop(() =>
                {
                    long balance = walletManager.GetBalances(walletName, walletAccount).Sum(x => x.AmountConfirmed);

                    return balance == (transferAmount + feeAmount);
                });
            }
        }

        [Fact]
        public async Task CanMineVotingRequestTransactionAsync()
        {
            var network = new TestPoACollateralNetwork(true, Guid.NewGuid().ToString());

            using (PoANodeBuilder builder = PoANodeBuilder.CreatePoANodeBuilder(this))
            {
                string walletName = "mywallet";
                string walletPassword = "pass";
                string walletAccount = "account 0";

                Money transferAmount = Money.Coins(0.01m);
                Money feeAmount = Money.Coins(0.0001m);

                var counterchainNetwork = new StraxTest();

                CoreNode nodeA = builder.CreatePoANodeWithCounterchain(network, counterchainNetwork, network.FederationKey1).WithWallet(walletPassword, walletName).Start();
                CoreNode nodeB = builder.CreatePoANodeWithCounterchain(network, counterchainNetwork, network.FederationKey2).WithWallet(walletPassword, walletName).Start();

                TestHelper.Connect(nodeA, nodeB);

                long toMineCount = network.Consensus.PremineHeight + network.Consensus.CoinbaseMaturity + 1 - nodeA.GetTip().Height;

                // Get coins on nodeA via the premine.
                await nodeA.MineBlocksAsync((int)toMineCount).ConfigureAwait(false);

                CoreNodePoAExtensions.WaitTillSynced(nodeA, nodeB);

                // Create voting-request transaction.
                var minerKey = new Key();
                var collateralKey = new Key();
                var request = new JoinFederationRequest(minerKey.PubKey, new Money(CollateralFederationMember.MinerCollateralAmount, MoneyUnit.BTC), collateralKey.PubKey.Hash);

                // In practice this signature will come from calling the counter-chain "signmessage" API.
                request.AddSignature(collateralKey.SignMessage(request.SignatureMessage));

                var encoder = new JoinFederationRequestEncoder(nodeA.FullNode.NodeService<Microsoft.Extensions.Logging.ILoggerFactory>());
                JoinFederationRequestResult result = JoinFederationRequestBuilder.BuildTransaction(nodeA.FullNode.WalletTransactionHandler(), this.poaNetwork, request, encoder, walletName, walletAccount, walletPassword);

                await nodeA.FullNode.NodeController<WalletController>().SendTransaction(new SendTransactionRequest(result.Transaction.ToHex()));

                TestBase.WaitLoop(() => nodeA.CreateRPCClient().GetRawMempool().Length == 1 && nodeB.CreateRPCClient().GetRawMempool().Length == 1);

                await nodeB.MineBlocksAsync((int)toMineCount).ConfigureAwait(false);

                TestBase.WaitLoop(() => nodeA.CreateRPCClient().GetRawMempool().Length == 0 && nodeB.CreateRPCClient().GetRawMempool().Length == 0);

                IWalletManager walletManager = nodeB.FullNode.NodeService<IWalletManager>();

                TestBase.WaitLoop(() =>
                {
                    long balance = walletManager.GetBalances(walletName, walletAccount).Sum(x => x.AmountConfirmed);

                    return balance == feeAmount;
                });

                Assert.Single(nodeA.FullNode.NodeService<VotingManager>().GetPendingPolls());
                Assert.Single(nodeB.FullNode.NodeService<VotingManager>().GetPendingPolls());
            }
        }

        public void Dispose()
        {
            this.builder.Dispose();
        }
    }
}
