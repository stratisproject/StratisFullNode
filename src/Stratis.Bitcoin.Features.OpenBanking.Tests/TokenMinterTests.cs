using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;
using Stratis.Bitcoin.Features.OpenBanking.TokenMinter;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Bitcoin.Features.OpenBanking.Tests
{
    public class TokenMinterTests
    {
        private readonly Network network;
        private readonly IServiceCollection serviceCollection;
        private readonly MockingContext mockingContext;

        public TokenMinterTests()
        {
            this.network = new CirrusTest();
            this.serviceCollection = new ServiceCollection()
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
                .AddSingleton<ILoggerFactory, CustomLoggerFactory>();
            this.mockingContext = new MockingContext(this.serviceCollection);
        }

        [Fact]
        public void CanCallTokenMintingTransaction()
        {
            var settings = this.mockingContext.GetService<OpenBankingSettings>();
            settings.WalletName = "default";
            settings.WalletAccount = "account 0";
            settings.WalletAddress = "tSk1UMHKSLPsjsRsTZ8D5GEkaULMTh8uRp";
            settings.WalletPassword = "password";

            string contractAddress = "tBHv3YgiSGZiohpEdTcsNbXivrCzxVReeP";
            this.mockingContext.GetService<Mock<IMetadataTracker>>().Setup(t => t.GetTracker(It.IsAny<MetadataTableNumber>())).Returns(new MetadataTrackerDefinition() { 
                Contract = contractAddress
            });

            this.serviceCollection.AddSingleton<ITokenMintingTransactionBuilder, TokenMintingTransactionBuilder>();

            var service = this.mockingContext.GetService<Mock<ISmartContractTransactionService>>();
            service.Setup(x => x.BuildCallTx(It.IsAny<BuildCallContractTransactionRequest>())).Returns(BuildCallContractTransactionResponse.Succeeded("methodName", new Transaction(), 10));

            var recipient = "tSk1UMHKSLPsjsRsTZ8D5GEkaULMTh8uRp";

            var deposit = new OpenBankDeposit()
            {
                Amount = new Money(100, MoneyUnit.BTC),
                Reference = recipient,
                TransactionId = "123",
                BookDateTimeUTC = DateTime.Parse("2001-2-3 4:5:6 +0"),
                ValueDateTimeUTC = DateTime.Parse("2001-2-3 4:7:6 +0")
            };

            var builder = this.mockingContext.GetService<ITokenMintingTransactionBuilder>();
            Transaction transaction = builder.BuildSignedTransaction(new OpenBankAccount(null, "123", MetadataTableNumber.GBPT, "GBP", contractAddress, 0), deposit) ;

            service.Verify(x => x.BuildCallTx(It.Is<BuildCallContractTransactionRequest>(y => y.AccountName == settings.WalletAccount && y.WalletName == settings.WalletName && y.Amount == "0" && y.Sender == settings.WalletAddress &&
                y.GasPrice == 100 && y.GasLimit == 250_000 && y.ContractAddress == contractAddress && y.FeeAmount == "0.04" && y.Password == settings.WalletPassword && 
                y.Parameters[0] == "9#" + recipient && y.Parameters[1] == "12#" + deposit.Amount.Satoshi.ToString() && y.Parameters[2] == "4#" + Encoders.ASCII.EncodeData(deposit.KeyBytes))));
        }

        [Fact]
        public async void TokenMintingServiceMintsBookedTransactionsAsync()
        {
            var settings = this.mockingContext.GetService<OpenBankingSettings>();
            settings.WalletName = "default";
            settings.WalletAccount = "account 0";
            settings.WalletAddress = "tSk1UMHKSLPsjsRsTZ8D5GEkaULMTh8uRp";
            settings.WalletPassword = "password";

            string contractAddress = "tBHv3YgiSGZiohpEdTcsNbXivrCzxVReeP";
            this.mockingContext.GetService<Mock<IMetadataTracker>>().Setup(t => t.GetTracker(It.IsAny<MetadataTableNumber>())).Returns(new MetadataTrackerDefinition()
            {
                Contract = contractAddress
            });

            this.serviceCollection
                .AddSingleton<IOpenBankingService, OpenBankingService>()
                .AddSingleton<ITokenMintingService, TokenMintingService>()
                .AddSingleton<ITokenMintingTransactionBuilder, TokenMintingTransactionBuilder>();

            this.mockingContext.GetService<Mock<ISmartContractTransactionService>>()
                .Setup(x => x.BuildCallTx(It.IsAny<BuildCallContractTransactionRequest>()))
                .Returns(BuildCallContractTransactionResponse.Succeeded("methodName", new Transaction(), 10));

            this.mockingContext.GetService<Mock<IOpenBankingClient>>()
                .Setup(c => c.GetTransactions(It.IsAny<IOpenBankAccount>(), It.IsAny<DateTime?>()))
                .Returns(JsonSerializer.Deserialize<OBGetTransactionsResponse>(OpenBankingServicesTests.GetSampleResourceString("BookedTransactionListValidReference.json")));

            var service = this.mockingContext.GetService<ITokenMintingService>();

            var account = new OpenBankAccount(null, "22289", MetadataTableNumber.GBPT, "GBP", contractAddress, 0);

            service.Register(account);

            await service.RunAsync(CancellationToken.None);

            // Get the deposit.
            OpenBankDeposit[] deposits = this.mockingContext.GetService<IOpenBankingService>().GetOpenBankDeposits(account, OpenBankDepositState.Booked).ToArray();

            Assert.Single(deposits);
            Assert.Null(deposits[0].Block);
            Assert.Equal("123", deposits[0].TransactionId);
            Assert.Equal(new Money(10, MoneyUnit.BTC), deposits[0].Amount);
            Assert.Equal(DateTime.Parse("2017-04-05T10:43:07+00:00").ToUniversalTime().Ticks, deposits[0].BookDateTimeUTC.ToUniversalTime().Ticks);
            Assert.Equal(DateTime.Parse("2017-04-05T10:45:22+00:00").ToUniversalTime().Ticks, deposits[0].ValueDateTimeUTC.ToUniversalTime().Ticks);
            Assert.Equal("tSk1UMHKSLPsjsRsTZ8D5GEkaULMTh8uRp", deposits[0].Reference);

            this.mockingContext.GetService<Mock<IBroadcasterManager>>().Verify(x => x.BroadcastTransactionAsync(It.Is<Transaction>(t => t.GetHash() == deposits[0].TxId)));
        }
    }
}
