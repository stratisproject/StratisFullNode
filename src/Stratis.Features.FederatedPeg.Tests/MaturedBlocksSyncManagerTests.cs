using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using Stratis.Bitcoin.AsyncWork;
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
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly IFederationGatewayClient federationGatewayClient;
        private readonly TestOnlyMaturedBlocksSyncManager syncManager;

        public MaturedBlocksSyncManagerTests()
        {
            this.asyncProvider = Substitute.For<IAsyncProvider>();
            this.crossChainTransferStore = Substitute.For<ICrossChainTransferStore>();
            this.federationGatewayClient = Substitute.For<IFederationGatewayClient>();

            this.syncManager = new TestOnlyMaturedBlocksSyncManager(this.asyncProvider, this.crossChainTransferStore, this.federationGatewayClient, new NodeLifetime());
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

        private class TestOnlyMaturedBlocksSyncManager : MaturedBlocksSyncManager
        {
            public TestOnlyMaturedBlocksSyncManager(IAsyncProvider asyncProvider, ICrossChainTransferStore crossChainTransferStore, IFederationGatewayClient federationGatewayClient, INodeLifetime nodeLifetime)
                : base(asyncProvider, crossChainTransferStore, federationGatewayClient, nodeLifetime, null, null, null, null)
            {
            }

            public Task<bool> ExposedSyncBatchOfBlocksAsync()
            {
                return this.SyncDepositsAsync();
            }
        }
    }
}
