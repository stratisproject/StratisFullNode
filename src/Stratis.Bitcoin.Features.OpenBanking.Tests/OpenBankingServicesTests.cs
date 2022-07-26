using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
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
            this.network = new CirrusTest();
            this.mockingContext = new MockingContext(new ServiceCollection()
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
                .AddSingleton<IOpenBankingService, OpenBankingService>()
            );
        }

        public static string GetSampleResourceString(string fileName)
        {
            using (Stream fileStream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Stratis.Bitcoin.Features.OpenBanking.Tests.OpenBankingAPISamples.{fileName}"))
            {
                using (StreamReader sr = new StreamReader(fileStream))
                {
                    return sr.ReadToEnd();
                }
            }
        }

        public static IEnumerable<object[]> SamplesAndExpectedStates()
        {
            yield return new object[] { "BookedTransactionListInvalidReference.json", OpenBankDepositState.Error };
            yield return new object[] { "BookedTransactionListValidReference.json", OpenBankDepositState.Booked};
            yield return new object[] { "PendingTransactionListInvalidReference.json", OpenBankDepositState.Pending };
        }

        [Theory]
        [MemberData(nameof(SamplesAndExpectedStates))]
        public void DepositSetToExpectedState(string sampleFile, OpenBankDepositState expectedState)
        {
            var mockOpenBankingClient = this.mockingContext.GetService<Mock<IOpenBankingClient>>();

            mockOpenBankingClient.Setup(m => m.GetTransactions(It.IsAny<IOpenBankAccount>(), It.IsAny<DateTime?>()))
                .Returns(JsonSerializer.Deserialize<OBGetTransactionsResponse>(GetSampleResourceString(sampleFile)));

            var openBankingService = this.mockingContext.GetService<IOpenBankingService>();

            OpenBankAccount openBankAccount = new OpenBankAccount(null, "123", MetadataTableNumber.GBPT, "GBP", "", 0);

            openBankingService.UpdateDeposits(openBankAccount);

            Assert.NotEmpty(openBankingService.GetOpenBankDeposits(openBankAccount, expectedState));
        }
    }
}