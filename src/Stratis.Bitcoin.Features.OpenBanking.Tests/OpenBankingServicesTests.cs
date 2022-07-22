using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Bitcoin.Features.OpenBanking.Tests
{
    public class OpenBankingServicesTests
    {
        private readonly Network network;
        private readonly MockingContext mockingContext;

        public OpenBankingServicesTests()
        {
            this.network = new CirrusMain();

            var mockingServices = new ServiceCollection()
                .AddSingleton(this.network)
                .AddSingleton(NodeSettings.Default(this.network))
                .AddSingleton(p => new StoreSettings(p.GetService<NodeSettings>())
                {
                    AddressIndex = true,
                    TxIndex = true
                })
                .AddSingleton(new DataFolder(TestBase.CreateTestDir(this)))
                .AddSingleton(new ChainIndexer(this.network))
                .AddSingleton<IDateTimeProvider, DateTimeProvider>()
                .AddSingleton<IAddressIndexer, AddressIndexer>()
                .AddSingleton<DBreezeSerializer>()
                .AddSingleton<IMetadataTracker, MetadataTracker>()
                .AddSingleton<IOpenBankingService, OpenBankingService>();

            this.mockingContext = new MockingContext(mockingServices);
        }

        private string GetSampleResourceString(string fileName)
        {
            using (Stream fileStream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Stratis.Bitcoin.Features.OpenBanking.Tests.OpenBankingAPISamples.{fileName}"))
            {
                using (StreamReader sr = new StreamReader(fileStream))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        [Fact]
        public void OpenBankDepositWithInvalidReferenceSetToErrorState()
        {
            var mockOpenBankingClient = this.mockingContext.GetService<Mock<IOpenBankingClient>>();

            mockOpenBankingClient.Setup(m => m.GetDeposits(It.IsAny<IOpenBankAccount>(), It.IsAny<DateTime?>())).Returns(GetSampleResourceString("TransactionList1.json"));

            var openBankingService = this.mockingContext.GetService<IOpenBankingService>();

            OpenBankAccount openBankAccount = new OpenBankAccount(null, "123", MetadataTrackerEnum.GBPT, "GBP");

            openBankingService.UpdateDeposits(openBankAccount);

            Assert.NotEmpty(openBankingService.GetOpenBankDeposits(openBankAccount, OpenBankDepositState.Error));
        }
    }
}