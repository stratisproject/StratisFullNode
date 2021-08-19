using System.Collections.Generic;
using System.Threading.Tasks;
using NBitcoin;
using NSubstitute;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Controllers;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.TargetChain;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    public class MaturedBlocksSyncManagerTests
    {
        private readonly IAsyncProvider asyncProvider;
        private readonly ChainIndexer chainIndexer;
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly IFederationGatewayClient federationGatewayClient;
        private IFederationWalletManager federationWalletManager;
        private IInitialBlockDownloadState initialBlockDownloadState;
        private readonly StraxTest network;
        private TestOnlyMaturedBlocksSyncManager syncManager;

        public MaturedBlocksSyncManagerTests()
        {
            this.network = new StraxTest();

            this.asyncProvider = Substitute.For<IAsyncProvider>();
            this.chainIndexer = new ChainIndexer(this.network);
            this.crossChainTransferStore = Substitute.For<ICrossChainTransferStore>();
            this.federationGatewayClient = Substitute.For<IFederationGatewayClient>();

            this.federationWalletManager = Substitute.For<IFederationWalletManager>();
            this.federationWalletManager.IsFederationWalletActive().Returns(true);
            this.federationWalletManager.IsSyncedWithChain().Returns(true);
            this.federationWalletManager.WalletTipHeight.Returns(0);

            this.initialBlockDownloadState = Substitute.For<IInitialBlockDownloadState>();
            this.initialBlockDownloadState.IsInitialBlockDownload().Returns(false);

            this.syncManager = new TestOnlyMaturedBlocksSyncManager(this.asyncProvider, this.chainIndexer, this.crossChainTransferStore, this.federationGatewayClient, this.federationWalletManager, this.initialBlockDownloadState, new NodeLifetime());
        }

        [Fact]
        public async Task BlocksAreRequestedIfThereIsSomethingToRequestAsync()
        {
            this.crossChainTransferStore.NextMatureDepositHeight.Returns(5);
            this.crossChainTransferStore.RecordLatestMatureDepositsAsync(null).ReturnsForAnyArgs(new RecordLatestMatureDepositsResult().Succeeded());

            var models = new List<MaturedBlockDepositsModel>() { new MaturedBlockDepositsModel(new MaturedBlockInfoModel(), new List<IDeposit>()) };
            var result = SerializableResult<List<MaturedBlockDepositsModel>>.Ok(models);
            this.federationGatewayClient.GetMaturedBlockDepositsAsync(0).ReturnsForAnyArgs(Task.FromResult(result));

            bool delayRequired = await this.syncManager.ExposedSyncBatchOfBlocksAsync();
            // Delay shouldn't be required because a non-empty list was provided.
            Assert.False(delayRequired);

            // Now provide empty list.
            result = SerializableResult<List<MaturedBlockDepositsModel>>.Ok(new List<MaturedBlockDepositsModel>() { });
            this.federationGatewayClient.GetMaturedBlockDepositsAsync(0).ReturnsForAnyArgs(Task.FromResult(result));

            bool delayRequired2 = await this.syncManager.ExposedSyncBatchOfBlocksAsync();
            // Delay is required because an empty list was provided.
            Assert.True(delayRequired2);

            // Now provide null.
            result = SerializableResult<List<MaturedBlockDepositsModel>>.Ok(null as List<MaturedBlockDepositsModel>);
            this.federationGatewayClient.GetMaturedBlockDepositsAsync(0).ReturnsForAnyArgs(Task.FromResult(result));

            bool delayRequired3 = await this.syncManager.ExposedSyncBatchOfBlocksAsync();
            // Delay is required because a null list was provided.
            Assert.True(delayRequired3);
        }

        [Fact]
        public async Task NodeIsInIBD_DelayRequiredAsync()
        {
            this.initialBlockDownloadState = Substitute.For<IInitialBlockDownloadState>();
            this.initialBlockDownloadState.IsInitialBlockDownload().Returns(true);
            this.syncManager = new TestOnlyMaturedBlocksSyncManager(this.asyncProvider, this.chainIndexer, this.crossChainTransferStore, this.federationGatewayClient, this.federationWalletManager, this.initialBlockDownloadState, new NodeLifetime());

            bool delayRequired = await this.syncManager.ExposedSyncBatchOfBlocksAsync();
            Assert.True(delayRequired);
        }

        [Fact]
        public async Task FederationWalletIsSyncing_DelayRequiredAsync()
        {
            this.initialBlockDownloadState = Substitute.For<IInitialBlockDownloadState>();
            this.initialBlockDownloadState.IsInitialBlockDownload().Returns(false);

            // Create chain of 15 blocks
            var testbase = new TestBase(this.network);
            List<Block> blocks = new TestBase(this.network).CreateBlocks(15);
            testbase.AppendBlocksToChain(this.chainIndexer, blocks);

            this.federationWalletManager = Substitute.For<IFederationWalletManager>();
            this.federationWalletManager.WalletTipHeight.Returns(0);

            this.syncManager = new TestOnlyMaturedBlocksSyncManager(this.asyncProvider, this.chainIndexer, this.crossChainTransferStore, this.federationGatewayClient, this.federationWalletManager, this.initialBlockDownloadState, new NodeLifetime());

            bool delayRequired = await this.syncManager.ExposedSyncBatchOfBlocksAsync();
            Assert.True(delayRequired);
        }

        private class TestOnlyMaturedBlocksSyncManager : MaturedBlocksSyncManager
        {
            public TestOnlyMaturedBlocksSyncManager(
                IAsyncProvider asyncProvider,
                ChainIndexer chainIndexer,
                ICrossChainTransferStore crossChainTransferStore,
                IFederationGatewayClient federationGatewayClient,
                IFederationWalletManager federationWalletManager,
                IInitialBlockDownloadState initialBlockDownloadState,
                INodeLifetime nodeLifetime)
                : base(
                      asyncProvider,
                      crossChainTransferStore,
                      federationGatewayClient,
                      federationWalletManager,
                      initialBlockDownloadState,
                      nodeLifetime,
                      null,
                      chainIndexer,
                      null,
                      null,
                      null)
            {
            }

            public Task<bool> ExposedSyncBatchOfBlocksAsync()
            {
                return this.SyncDepositsAsync();
            }
        }
    }
}