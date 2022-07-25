using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.OpenBanking.OpenBanking;
using Stratis.Bitcoin.Features.OpenBanking.TokenMinter;
using Stratis.Bitcoin.Features.SmartContracts.MetadataTracker;
using Stratis.Bitcoin.Features.SmartContracts.Models;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Bitcoin.Features.OpenBanking.Tests
{
    public class TokenMinterTests
    {
        private readonly Network network;
        private readonly MockingContext mockingContext;

        public TokenMinterTests()
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
                .AddSingleton<ITokenMintingTransactionBuilder, TokenMintingTransactionBuilder>()
            );
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
            this.mockingContext.GetService<Mock<IMetadataTracker>>().Setup(t => t.GetTracker(It.IsAny<MetadataTrackerEnum>())).Returns(new MetadataTrackerDefinition() { 
                Contract = contractAddress
            });

            var builder = this.mockingContext.GetService<TokenMintingTransactionBuilder>();

            var service = this.mockingContext.GetService<Mock<ISmartContractTransactionService>>();
            service.Setup(x => x.BuildCallTx(It.IsAny<BuildCallContractTransactionRequest>())).Returns(BuildCallContractTransactionResponse.Succeeded("methodName", new Transaction(), 10));

            var recipient = "tSk1UMHKSLPsjsRsTZ8D5GEkaULMTh8uRp";

            var deposit = new OpenBankDeposit()
            {
                Amount = new Money(100, MoneyUnit.BTC),
                Reference = recipient,
                TransactionId = "123",
                BookDateTimeUTC = DateTime.Parse("2001-2-3 4:5:6 +0")
            };

            Transaction transaction = builder.BuildSignedTransaction(new OpenBankAccount(null, "123", MetadataTrackerEnum.GBPT, "GBP"), deposit) ;

            service.Verify(x => x.BuildCallTx(It.Is<BuildCallContractTransactionRequest>(y => y.AccountName == settings.WalletAccount && y.WalletName == settings.WalletName && y.Amount == "0" && y.Sender == settings.WalletAddress &&
                y.GasPrice == 100 && y.GasLimit == 250_000 && y.ContractAddress == contractAddress && y.FeeAmount == "0.04" && y.Password == settings.WalletPassword && 
                y.Parameters[0] == "9#" + recipient && y.Parameters[1] == "12#" + deposit.Amount.Satoshi.ToString() && y.Parameters[2] == "4#" + Encoders.ASCII.EncodeData(deposit.KeyBytes))));
        }
    }
}
