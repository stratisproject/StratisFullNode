using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DBreeze.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using NBitcoin.Policy;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Services;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Tests.Common.Logging;
using Stratis.Bitcoin.Tests.Wallet.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.SQLiteWalletRepository;
using Xunit;

namespace Stratis.Bitcoin.Features.Wallet.Tests
{
    public class WalletTransactionHandlerTest : LogsTestBase
    {
        private readonly string costlyOpReturnData;
        private readonly ILoggerFactory loggerFactory = new ExtendedLoggerFactory();
        private readonly IScriptAddressReader scriptAddressReader;
        private readonly StandardTransactionPolicy standardTransactionPolicy;

        public WalletTransactionHandlerTest()
        {
            // adding this data to the transaction output should increase the fee
            // 83 is the max size for the OP_RETURN script => 80 is the max for the content of the script
            byte[] maxQuantityOfBytes = Enumerable.Range(0, 80).Select(Convert.ToByte).ToArray();
            this.costlyOpReturnData = Encoding.UTF8.GetString(maxQuantityOfBytes);
            this.standardTransactionPolicy = new StandardTransactionPolicy(this.Network);
            this.scriptAddressReader = new ScriptAddressReader();
        }

        [Fact]
        public void BuildTransactionThrowsWalletExceptionWhenMoneyIsZero()
        {
            Assert.Throws<WalletException>(() =>
            {
                var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

                var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, new Mock<IWalletManager>().Object, new Mock<IWalletFeePolicy>().Object, this.Network, this.standardTransactionPolicy, reserveUtxoService);

                Transaction result = walletTransactionHandler.BuildTransaction(CreateContext(this.Network, new WalletAccountReference(), "password", new Script(), Money.Zero, FeeType.Medium, 2));
            });
        }

        [Fact]
        public void BuildTransactionNoSpendableTransactionsThrowsWalletException()
        {
            Assert.Throws<WalletException>(() =>
            {
                WalletTransactionHandlerTestContext testContext = SetupWallet(coinBaseBlocks: 0);
                testContext.WalletTransactionHandler.BuildTransaction(CreateContext(this.Network, testContext.WalletReference, "password", new Script(), new Money(500), FeeType.Medium, 2));
            });
        }

        [Fact]
        public void BuildTransactionFeeTooLowDefaultsToMinimumFee()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet(new FeeRate(0));

            TransactionBuildContext context = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0);
            Transaction transactionResult = testContext.WalletTransactionHandler.BuildTransaction(context);

            Assert.Equal(new Money(this.Network.MinTxFee, MoneyUnit.Satoshi), context.TransactionFee);
        }

        [Fact]
        public void BuildTransactionNoChangeAdressSpecified()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            TransactionBuildContext context = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0);
            Transaction transactionResult = testContext.WalletTransactionHandler.BuildTransaction(context);

            Transaction result = this.Network.CreateTransaction(transactionResult.ToHex());
            HdAccount account = testContext.Wallet.GetAccount("account1");
            Script expectedChangeAddressScript = account.InternalAddresses.First().ScriptPubKey;

            Assert.Single(result.Inputs);
            Assert.Equal(testContext.AddressTransaction.Id, result.Inputs[0].PrevOut.Hash);

            Assert.Equal(2, result.Outputs.Count);
            TxOut output = result.Outputs[0];
            Assert.Equal((testContext.AddressTransaction.Amount - context.TransactionFee - 7500), output.Value);
            Assert.Equal(expectedChangeAddressScript, output.ScriptPubKey);

            output = result.Outputs[1];
            Assert.Equal(7500, output.Value);
            Assert.Equal(testContext.DestinationKeys.PubKey.ScriptPubKey, output.ScriptPubKey);

            Assert.Equal(testContext.AddressTransaction.Amount - context.TransactionFee, result.TotalOut);
            Assert.NotNull(transactionResult.GetHash());
            Assert.Equal(result.GetHash(), transactionResult.GetHash());
        }

        [Fact]
        public void BuildTransactionUsesGivenChangeAddress()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();
            TransactionBuildContext context = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0);

            var key = new Key();
            BitcoinPubKeyAddress address = key.PubKey.GetAddress(this.Network);
            HdAddress changeAddress = context.ChangeAddress = new HdAddress
            {
                Index = 0,
                HdPath = $"m/44'/0'/0'/0/0",
                Address = address.ToString(),
                Pubkey = key.PubKey.ScriptPubKey,
                ScriptPubKey = address.ScriptPubKey
            };

            Transaction transactionResult = testContext.WalletTransactionHandler.BuildTransaction(context);

            Transaction result = this.Network.CreateTransaction(transactionResult.ToHex());

            Assert.Single(result.Inputs);
            Assert.Equal(testContext.AddressTransaction.Id, result.Inputs[0].PrevOut.Hash);

            Assert.Equal(2, result.Outputs.Count);
            TxOut output = result.Outputs[0];
            Assert.Equal((testContext.AddressTransaction.Amount - context.TransactionFee - 7500), output.Value);
            Assert.Equal(changeAddress.ScriptPubKey, output.ScriptPubKey);

            output = result.Outputs[1];
            Assert.Equal(7500, output.Value);
            Assert.Equal(testContext.DestinationKeys.PubKey.ScriptPubKey, output.ScriptPubKey);

            Assert.Equal(testContext.AddressTransaction.Amount - context.TransactionFee, result.TotalOut);
            Assert.NotNull(transactionResult.GetHash());
            Assert.Equal(result.GetHash(), transactionResult.GetHash());
        }

        [Fact]
        public void BuildTransaction_When_OpReturnData_Is_Empty_Should_Not_Add_Extra_Output()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            string opReturnData = "";

            TransactionBuildContext context = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, opReturnData);
            Transaction transactionResult = testContext.WalletTransactionHandler.BuildTransaction(context);

            transactionResult.Outputs.Where(o => o.ScriptPubKey.IsUnspendable).Should()
                .BeEmpty("because opReturnData is empty");
        }

        [Fact]
        public void BuildTransaction_When_OpReturnData_Is_Null_Should_Not_Add_Extra_Output()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            string opReturnData = null;

            TransactionBuildContext context = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, opReturnData);
            Transaction transactionResult = testContext.WalletTransactionHandler.BuildTransaction(context);

            transactionResult.Outputs.Where(o => o.ScriptPubKey.IsUnspendable).Should()
                .BeEmpty("because opReturnData is null");
        }

        [Fact]
        public void BuildTransaction_When_OpReturnData_Is_Neither_Null_Nor_Empty_Should_Add_Extra_Output_With_Data()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            string opReturnData = "some extra transaction info";
            byte[] expectedBytes = Encoding.UTF8.GetBytes(opReturnData);

            TransactionBuildContext context = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, opReturnData);
            Transaction transactionResult = testContext.WalletTransactionHandler.BuildTransaction(context);

            IEnumerable<TxOut> unspendableOutputs = transactionResult.Outputs.Where(o => o.ScriptPubKey.IsUnspendable).ToList();
            unspendableOutputs.Count().Should().Be(1);
            unspendableOutputs.Single().Value.Should().Be(Money.Zero);

            IEnumerable<Op> ops = unspendableOutputs.Single().ScriptPubKey.ToOps();
            ops.Count().Should().Be(2);
            ops.First().Code.Should().Be(OpcodeType.OP_RETURN);
            ops.Last().PushData.Should().BeEquivalentTo(expectedBytes);
        }

        [Fact]
        public void BuildTransaction_When_OpReturnAmount_Is_Populated_Should_Add_Extra_Output_With_Data_And_Amount()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            string opReturnData = "some extra transaction info";
            byte[] expectedBytes = Encoding.UTF8.GetBytes(opReturnData);

            TransactionBuildContext context = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, opReturnData);

            context.OpReturnAmount = Money.Coins(0.0001m);

            Transaction transactionResult = testContext.WalletTransactionHandler.BuildTransaction(context);

            IEnumerable<TxOut> unspendableOutputs = transactionResult.Outputs.Where(o => o.ScriptPubKey.IsUnspendable).ToList();
            unspendableOutputs.Count().Should().Be(1);
            unspendableOutputs.Single().Value.Should().Be(Money.Coins(0.0001m));

            IEnumerable<Op> ops = unspendableOutputs.Single().ScriptPubKey.ToOps();
            ops.Count().Should().Be(2);
            ops.First().Code.Should().Be(OpcodeType.OP_RETURN);
            ops.Last().PushData.Should().BeEquivalentTo(expectedBytes);
        }

        [Fact]
        public void BuildTransaction_When_OpReturnData_Is_Too_Long_Should_Fail_With_Helpful_Message()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            byte[] eightyOneBytes = Encoding.UTF8.GetBytes(this.costlyOpReturnData).Concat(Convert.ToByte(1));
            string tooLongOpReturnString = Encoding.UTF8.GetString(eightyOneBytes);

            TransactionBuildContext context = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, tooLongOpReturnString);
            new Action(() => testContext.WalletTransactionHandler.BuildTransaction(context))
                .Should().Throw<ArgumentOutOfRangeException>()
                .And.Message.Should().Contain(" maximum size of 83");

        }

        /// <summary>
        /// Adds inputs to a transaction until it has enough in value to meet its out value.
        /// </summary>
        /// <param name="walletTransactionHandler">See <see cref="WalletTransactionHandler"/>.</param>
        /// <param name="context">The context associated with the current transaction being built.</param>
        /// <param name="transaction">The transaction that will have more inputs added to it.</param>
        /// <remarks>
        /// This will not modify existing inputs, and will add at most one change output to the outputs.
        /// No existing outputs will be modified unless <see cref="Recipient.SubtractFeeFromAmount"/> is specified.
        /// Note that inputs which were signed may need to be resigned after completion since in/outputs have been added.
        /// The inputs added may be signed depending on whether a <see cref="TransactionBuildContext.WalletPassword"/> is passed.
        /// Note that all existing inputs must have their previous output transaction be in the wallet.
        /// </remarks>
        private void FundTransaction(WalletTransactionHandler walletTransactionHandler, TransactionBuildContext context, Transaction transaction)
        {
            if (context.Recipients.Any())
                throw new WalletException("Adding outputs is not allowed.");

            // Turn the txout set into a Recipient array.
            context.Recipients.AddRange(transaction.Outputs
                .Select(s => new Recipient
                {
                    ScriptPubKey = s.ScriptPubKey,
                    Amount = s.Value,
                    SubtractFeeFromAmount = false // default for now
                }));

            context.AllowOtherInputs = true;

            foreach (TxIn transactionInput in transaction.Inputs)
                context.SelectedInputs.Add(transactionInput.PrevOut);

            Transaction newTransaction = walletTransactionHandler.BuildTransaction(context);

            if (context.ChangeAddress != null)
            {
                // find the position of the change and move it over.
                int index = 0;
                foreach (TxOut newTransactionOutput in newTransaction.Outputs)
                {
                    if (newTransactionOutput.ScriptPubKey == context.ChangeAddress.ScriptPubKey)
                    {
                        transaction.Outputs.Insert(index, newTransactionOutput);
                    }

                    index++;
                }
            }

            // TODO: copy the new output amount size (this also includes spreading the fee over all outputs)

            // copy all the inputs from the new transaction.
            foreach (TxIn newTransactionInput in newTransaction.Inputs)
            {
                if (!context.SelectedInputs.Contains(newTransactionInput.PrevOut))
                {
                    transaction.Inputs.Add(newTransactionInput);

                    // TODO: build a mechanism to lock inputs
                }
            }
        }

        [Fact]
        public void FundTransaction_Given__a_wallet_has_enough_inputs__When__adding_inputs_to_an_existing_transaction__Then__the_transaction_is_funded_successfully()
        {
            // Wallet with 4 coinbase outputs of 50 = 200 Bitcoin.
            WalletTransactionHandlerTestContext testContext = SetupWallet(coinBaseBlocks: 4);

            (PubKey PubKey, BitcoinPubKeyAddress Address) destinationKeys1 = WalletTestsHelpers.GenerateAddressKeys(testContext.Wallet, testContext.AccountKeys.ExtPubKey, "0/1");
            (PubKey PubKey, BitcoinPubKeyAddress Address) destinationKeys2 = WalletTestsHelpers.GenerateAddressKeys(testContext.Wallet, testContext.AccountKeys.ExtPubKey, "0/2");
            (PubKey PubKey, BitcoinPubKeyAddress Address) destinationKeys3 = WalletTestsHelpers.GenerateAddressKeys(testContext.Wallet, testContext.AccountKeys.ExtPubKey, "0/3");

            // create a trx with 3 outputs 50 + 50 + 50 = 150 BTC
            var context = new TransactionBuildContext(this.Network)
            {
                AccountReference = testContext.WalletReference,
                MinConfirmations = 0,
                FeeType = FeeType.Low,
                WalletPassword = "password",
                Recipients = new[]
                {
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destinationKeys1.PubKey.ScriptPubKey },
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destinationKeys2.PubKey.ScriptPubKey },
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destinationKeys3.PubKey.ScriptPubKey }
                }.ToList()
            };

            Transaction fundTransaction = testContext.WalletTransactionHandler.BuildTransaction(context);
            Assert.Equal(4, fundTransaction.Inputs.Count); // 4 inputs
            Assert.Equal(4, fundTransaction.Outputs.Count); // 3 outputs with change

            // remove the change output
            fundTransaction.Outputs.Remove(fundTransaction.Outputs.First(f => f.ScriptPubKey == context.ChangeAddress.ScriptPubKey));
            // remove 3 inputs they will be added back by fund transaction
            fundTransaction.Inputs.RemoveAt(3);
            fundTransaction.Inputs.RemoveAt(2);
            fundTransaction.Inputs.RemoveAt(1);
            Assert.Single(fundTransaction.Inputs); // 4 inputs

            Transaction fundTransactionClone = this.Network.CreateTransaction(fundTransaction.ToBytes());
            var fundContext = new TransactionBuildContext(this.Network)
            {
                AccountReference = testContext.WalletReference,
                MinConfirmations = 0,
                FeeType = FeeType.Low,
                WalletPassword = "password",
                Recipients = new List<Recipient>()
            };

            var overrideFeeRate = new FeeRate(20000);
            fundContext.OverrideFeeRate = overrideFeeRate;
            FundTransaction(testContext.WalletTransactionHandler, fundContext, fundTransaction);

            foreach (TxIn input in fundTransactionClone.Inputs) // all original inputs are still in the trx
                Assert.Contains(fundTransaction.Inputs, a => a.PrevOut == input.PrevOut);

            Assert.Equal(4, fundTransaction.Inputs.Count); // we expect 4 inputs
            Assert.Equal(4, fundTransaction.Outputs.Count); // we expect 4 outputs
            Assert.Equal(new Money(200, MoneyUnit.BTC) - fundContext.TransactionFee, fundTransaction.TotalOut);

            Assert.Contains(fundTransaction.Outputs, a => a.ScriptPubKey == destinationKeys1.PubKey.ScriptPubKey);
            Assert.Contains(fundTransaction.Outputs, a => a.ScriptPubKey == destinationKeys2.PubKey.ScriptPubKey);
            Assert.Contains(fundTransaction.Outputs, a => a.ScriptPubKey == destinationKeys3.PubKey.ScriptPubKey);
        }

        [Fact]
        public void Given_AnInvalidAccountIsUsed_When_GetMaximumSpendableAmountIsCalled_Then_AnExceptionIsThrown()
        {
            DataFolder dataFolder = CreateDataFolder(this);

            var chain = new ChainIndexer(this.Network);
            IWalletRepository walletRepository = new SQLiteWalletRepository(this.LoggerFactory.Object, dataFolder, this.Network, DateTimeProvider.Default, new ScriptAddressReader());
            var walletManager = new WalletManager(this.LoggerFactory.Object, this.Network, chain, new WalletSettings(NodeSettings.Default(this.Network)),
                dataFolder, new Mock<IWalletFeePolicy>().Object, DateTimeProvider.Default, walletRepository);

            walletManager.Start();

            var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

            var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, walletManager, It.IsAny<WalletFeePolicy>(), this.Network, this.standardTransactionPolicy, reserveUtxoService);

            Wallet wallet = WalletTestsHelpers.CreateWallet("wallet1", walletRepository);
            HdAccount account = wallet.AddNewAccount((ExtPubKey)null, accountName: "account 1");

            Exception ex = Assert.Throws<WalletException>(() => walletTransactionHandler.GetMaximumSpendableAmount(new WalletAccountReference("wallet1", "noaccount"), FeeType.Low, true));
            Assert.NotNull(ex);
            Assert.NotNull(ex.Message);
            Assert.NotEqual(string.Empty, ex.Message);
            Assert.IsType<WalletException>(ex);
        }

        /// <summary>
        /// Given_GetMaximumSpendableAmountIsCalled_When_ThereAreNoSpendableFound_Then_MaxAmountReturnsAsZero
        /// </summary>
        [Fact]
        public void WalletMaxSpendableTest3()
        {
            DataFolder dataFolder = CreateDataFolder(this);

            IWalletRepository walletRepository = new SQLiteWalletRepository(this.LoggerFactory.Object, dataFolder, this.Network, DateTimeProvider.Default, new ScriptAddressReader())
            {
                TestMode = true
            };

            var walletManager = new WalletManager(this.LoggerFactory.Object, this.Network, new ChainIndexer(this.Network), new WalletSettings(NodeSettings.Default(this.Network)),
                dataFolder, new Mock<IWalletFeePolicy>().Object, DateTimeProvider.Default, walletRepository);

            walletManager.Start();

            var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

            var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, walletManager, It.IsAny<WalletFeePolicy>(), this.Network, this.standardTransactionPolicy, reserveUtxoService);

            (Wallet wallet, ExtKey extKey) = WalletTestsHelpers.GenerateBlankWalletWithExtKey("wallet1", "password", walletRepository);

            // Passing a null extpubkey into account creation causes problems later, so we need to obtain it first
            int accountIndex = 0;
            ExtKey addressExtKey = extKey.Derive(new KeyPath($"m/44'/{this.Network.Consensus.CoinType}'/{accountIndex}'"));
            ExtPubKey extPubKey = addressExtKey.Neuter();

            HdAccount account = wallet.AddNewAccount(extPubKey, accountName: "account 1");

            HdAddress accountAddress1 = account.ExternalAddresses.First();
            accountAddress1.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(1), new Money(15000), 1, new SpendingDetails()));
            accountAddress1.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(2), new Money(10000), 1, new SpendingDetails()));

            HdAddress accountAddress2 = account.InternalAddresses.First();
            accountAddress2.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(3), new Money(20000), 3, new SpendingDetails()));
            accountAddress2.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(4), new Money(120000), 4, new SpendingDetails()));
            
            (Money max, Money fee) result = walletTransactionHandler.GetMaximumSpendableAmount(new WalletAccountReference("wallet1", "account 1"), FeeType.Low, true);
            Assert.Equal(Money.Zero, result.max);
            Assert.Equal(Money.Zero, result.fee);
        }

        [Fact]
        public void GetMaximumSpendableAmountReturnsAsZeroIfNoConfirmedTransactions()
        {
            DataFolder dataFolder = CreateDataFolder(this);

            IWalletRepository walletRepository = new SQLiteWalletRepository(this.LoggerFactory.Object, dataFolder, this.Network, DateTimeProvider.Default, new ScriptAddressReader())
            {
                TestMode = true
            };

            var walletManager = new WalletManager(this.LoggerFactory.Object, this.Network, new ChainIndexer(this.Network), new WalletSettings(NodeSettings.Default(this.Network)),
                dataFolder, new Mock<IWalletFeePolicy>().Object, DateTimeProvider.Default, walletRepository);

            var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

            var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, walletManager, It.IsAny<WalletFeePolicy>(), this.Network, this.standardTransactionPolicy, reserveUtxoService);

            walletManager.Start();

            (Wallet wallet, ExtKey extKey) = WalletTestsHelpers.GenerateBlankWalletWithExtKey("wallet1", "password", walletRepository);

            // Passing a null extpubkey into account creation causes problems later, so we need to obtain it first
            int accountIndex = 0;
            ExtKey addressExtKey = extKey.Derive(new KeyPath($"m/44'/{this.Network.Consensus.CoinType}'/{accountIndex}'"));
            ExtPubKey extPubKey = addressExtKey.Neuter();

            HdAccount account = wallet.AddNewAccount(extPubKey, accountName: "account 1");

            HdAddress accountAddress1 = account.ExternalAddresses.First();
            accountAddress1.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(1), new Money(15000), null));
            accountAddress1.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(2), new Money(10000), null));

            HdAddress accountAddress2 = account.InternalAddresses.First();
            accountAddress2.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(3), new Money(20000), null));
            accountAddress2.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(4), new Money(120000), null));

            (Money max, Money fee) result = walletTransactionHandler.GetMaximumSpendableAmount(new WalletAccountReference("wallet1", "account 1"), FeeType.Low, false);
            Assert.Equal(Money.Zero, result.max);
            Assert.Equal(Money.Zero, result.fee);
        }

        /// <summary>
        /// GetMaximumSpendableAmountReturnsSumOfUnconfirmedWhenNoConfirmedSpendableFoundAndUnconfirmedAllowed
        /// </summary>
        [Fact]
        public void WalletMaxSpendableTest1()
        {
            DataFolder dataFolder = CreateDataFolder(this);

            var walletFeePolicy = new Mock<IWalletFeePolicy>();
            walletFeePolicy.Setup(w => w.GetFeeRate(FeeType.Low.ToConfirmations())).Returns(new FeeRate(20000));

            IWalletRepository walletRepository = new SQLiteWalletRepository(this.LoggerFactory.Object, dataFolder, this.Network, DateTimeProvider.Default, new ScriptAddressReader())
            {
                TestMode = true
            };

            var walletManager = new WalletManager(this.LoggerFactory.Object, this.Network, new ChainIndexer(this.Network), new WalletSettings(NodeSettings.Default(this.Network)),
                dataFolder, new Mock<IWalletFeePolicy>().Object, DateTimeProvider.Default, walletRepository);

            walletManager.Start();

            var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

            var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, walletManager, walletFeePolicy.Object, this.Network, this.standardTransactionPolicy, reserveUtxoService);
            
            (Wallet wallet, ExtKey extKey) = WalletTestsHelpers.GenerateBlankWalletWithExtKey("wallet1", "password", walletRepository);

            // Passing a null extpubkey into account creation causes problems later, so we need to obtain it first
            int accountIndex = 0;
            ExtKey addressExtKey = extKey.Derive(new KeyPath($"m/44'/{this.Network.Consensus.CoinType}'/{accountIndex}'"));
            ExtPubKey extPubKey = addressExtKey.Neuter();

            HdAccount account = wallet.AddNewAccount(extPubKey, accountName: "account 1");

            HdAddress accountAddress1 = account.ExternalAddresses.First();
            accountAddress1.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(1), new Money(15000), null, null, null, accountAddress1.ScriptPubKey));
            accountAddress1.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(2), new Money(10000), null, null, null, accountAddress1.ScriptPubKey));

            HdAddress accountAddress2 = account.InternalAddresses.Skip(1).First();
            accountAddress2.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(3), new Money(20000), null, null, null, accountAddress2.ScriptPubKey));
            accountAddress2.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(4), new Money(120000), null, null, null, accountAddress2.ScriptPubKey));
            
            (Money max, Money fee) result = walletTransactionHandler.GetMaximumSpendableAmount(new WalletAccountReference("wallet1", "account 1"), FeeType.Low, true);
            Assert.Equal(new Money(165000), result.max + result.fee);
        }

        /// <summary>
        /// Given_GetMaximumSpendableAmountIsCalled_When_ThereAreNoTransactions_Then_MaxAmountReturnsAsZero
        /// </summary>
        [Fact]
        public void WalletMaxSpendableTest2()
        {
            DataFolder dataFolder = CreateDataFolder(this);

            IWalletRepository walletRepository = new SQLiteWalletRepository(this.LoggerFactory.Object, dataFolder, this.Network, DateTimeProvider.Default, new ScriptAddressReader())
            {
                TestMode = true
            };

            var walletManager = new WalletManager(this.LoggerFactory.Object, this.Network, new ChainIndexer(this.Network), new WalletSettings(NodeSettings.Default(this.Network)),
                dataFolder, new Mock<IWalletFeePolicy>().Object, DateTimeProvider.Default, walletRepository);

            walletManager.Start();

            var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

            var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, walletManager, It.IsAny<WalletFeePolicy>(), this.Network, this.standardTransactionPolicy, reserveUtxoService);

            (Wallet wallet, ExtKey extKey) = WalletTestsHelpers.GenerateBlankWalletWithExtKey("wallet1", "password", walletRepository);

            // Passing a null extpubkey into account creation causes problems later, so we need to obtain it first
            int accountIndex = 0;
            ExtKey addressExtKey = extKey.Derive(new KeyPath($"m/44'/{this.Network.Consensus.CoinType}'/{accountIndex}'"));
            ExtPubKey extPubKey = addressExtKey.Neuter();

            wallet.AddNewAccount(extPubKey, accountName: "account 1");

            (Money max, Money fee) result = walletTransactionHandler.GetMaximumSpendableAmount(new WalletAccountReference("wallet1", "account 1"), FeeType.Low, true);
            Assert.Equal(Money.Zero, result.max);
            Assert.Equal(Money.Zero, result.fee);
        }

        /// <summary>
        /// Tests the <see cref="WalletTransactionHandler.EstimateFee(TransactionBuildContext)"/> method by
        /// comparing it's fee calculation with the transaction fee computed for the same tx in the
        /// <see cref="WalletTransactionHandler.BuildTransaction(TransactionBuildContext)"/> method.
        /// </summary>
        [Fact]
        public void EstimateFeeWithLowFeeMatchesBuildTxLowFee()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            // Context to build requires password in order to sign transaction.
            TransactionBuildContext buildContext = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0);
            testContext.WalletTransactionHandler.BuildTransaction(buildContext);

            // Context for estimate does not need password.
            TransactionBuildContext estimateContext = CreateContext(this.Network, testContext.WalletReference, null, testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0);
            Money fee = testContext.WalletTransactionHandler.EstimateFee(estimateContext);

            Assert.Equal(fee, buildContext.TransactionFee);
        }

        /// <summary>
        /// Tests the <see cref="WalletTransactionHandler.EstimateFee(TransactionBuildContext)"/> method by
        /// comparing it's fee calculation with the transaction fee computed for the same tx in the
        /// <see cref="WalletTransactionHandler.BuildTransaction(TransactionBuildContext)"/> method.
        /// </summary>
        [Fact]
        public void EstimateFee_WithLowFee_Matches_BuildTransaction_WithLowFee_With_Long_OpReturnData_added()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            // Context to build requires password in order to sign transaction.
            TransactionBuildContext buildContext = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, this.costlyOpReturnData);
            testContext.WalletTransactionHandler.BuildTransaction(buildContext);

            // Context for estimate does not need password.
            TransactionBuildContext estimateContext = CreateContext(this.Network, testContext.WalletReference, null, testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, this.costlyOpReturnData);
            Money feeEstimate = testContext.WalletTransactionHandler.EstimateFee(estimateContext);

            feeEstimate.Should().Be(buildContext.TransactionFee);
        }

        /// <summary>
        /// Make sure that if you add data to the transaction in an OP_RETURN the estimated fee increases
        /// </summary>
        [Fact]
        public void EstimateFee_Without_OpReturnData_Should_Be_Less_Than_Estimate_Fee_With_Costly_OpReturnData()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            // Context with OpReturnData
            TransactionBuildContext estimateContextWithOpReturn = CreateContext(this.Network, testContext.WalletReference, null, testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, this.costlyOpReturnData);
            Money feeEstimateWithOpReturn = testContext.WalletTransactionHandler.EstimateFee(estimateContextWithOpReturn);

            // Context without OpReturnData
            TransactionBuildContext estimateContextWithoutOpReturn = CreateContext(this.Network, testContext.WalletReference, null, testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, null);
            Money feeEstimateWithoutOpReturn = testContext.WalletTransactionHandler.EstimateFee(estimateContextWithoutOpReturn);

            feeEstimateWithOpReturn.Should().NotBe(feeEstimateWithoutOpReturn);
            feeEstimateWithoutOpReturn.Satoshi.Should().BeLessThan(feeEstimateWithOpReturn.Satoshi);
        }

        /// <summary>
        /// Make sure that if you add data to the transaction in an OP_RETURN the actual fee increases
        /// </summary>
        [Fact]
        public void Actual_Fee_Without_OpReturnData_Should_Be_Less_Than_Actual_Fee_With_Costly_OpReturnData()
        {
            WalletTransactionHandlerTestContext testContext = SetupWallet();

            // Context with OpReturnData
            TransactionBuildContext contextWithOpReturn = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, this.costlyOpReturnData);
            testContext.WalletTransactionHandler.BuildTransaction(contextWithOpReturn);

            // Context without OpReturnData
            TransactionBuildContext contextWithoutOpReturn = CreateContext(this.Network, testContext.WalletReference, "password", testContext.DestinationKeys.PubKey.ScriptPubKey, new Money(7500), FeeType.Low, 0, null);
            testContext.WalletTransactionHandler.BuildTransaction(contextWithoutOpReturn);

            contextWithoutOpReturn.TransactionFee.Should().NotBe(contextWithOpReturn.TransactionFee);
            contextWithoutOpReturn.TransactionFee.Satoshi.Should().BeLessThan(contextWithOpReturn.TransactionFee.Satoshi);
        }

        [Fact]
        public void When_BuildTransactionIsCalled_Then_FeeIsDeductedFromAmountsInTransaction()
        {
            DataFolder dataFolder = CreateDataFolder(this);

            IWalletRepository walletRepository = new SQLiteWalletRepository(this.LoggerFactory.Object, dataFolder, this.Network, DateTimeProvider.Default, new ScriptAddressReader())
            {
                TestMode = true
            };

            var walletFeePolicy = new Mock<IWalletFeePolicy>();
            walletFeePolicy.Setup(w => w.GetFeeRate(FeeType.Low.ToConfirmations())).Returns(new FeeRate(20000));

            var walletManager = new WalletManager(this.LoggerFactory.Object, this.Network, new ChainIndexer(this.Network), new WalletSettings(NodeSettings.Default(this.Network)),
                dataFolder, walletFeePolicy.Object, DateTimeProvider.Default, walletRepository);

            walletManager.Start();

            var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

            var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, walletManager, walletFeePolicy.Object, this.Network, this.standardTransactionPolicy, reserveUtxoService);

            (Wallet wallet, ExtKey extKey) = WalletTestsHelpers.GenerateBlankWalletWithExtKey("myWallet1", "password", walletRepository);

            walletManager.Wallets.Add(wallet);

            int accountIndex = 0;
            ExtKey addressExtKey = extKey.Derive(new KeyPath($"m/44'/{this.Network.Consensus.CoinType}'/{accountIndex}'"));
            ExtPubKey extPubKey = addressExtKey.Neuter();

            HdAccount account = wallet.AddNewAccount(extPubKey, accountName: "account1");

            var address = account.ExternalAddresses.First();
            var destination = account.InternalAddresses.First();
            var destination2 = account.InternalAddresses.Skip(1).First();
            var destination3 = account.InternalAddresses.Skip(2).First();

            // Wallet with 4 coinbase outputs of 50 = 200.
            var chain = new ChainIndexer(wallet.Network);
            WalletTestsHelpers.AddBlocksWithCoinbaseToChain(wallet.Network, chain, address, 4);

            var walletReference = new WalletAccountReference
            {
                AccountName = "account1",
                WalletName = "myWallet1"
            };

            // Create a transaction with 3 outputs 50 + 50 + 50 = 150 but with fees charged to recipients.
            var context = new TransactionBuildContext(this.Network)
            {
                AccountReference = walletReference,
                MinConfirmations = 0,
                TransactionFee = Money.Coins(0.0001m),
                WalletPassword = "password",
                Recipients = new[]
                {
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destination.ScriptPubKey, SubtractFeeFromAmount = true },
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destination2.ScriptPubKey, SubtractFeeFromAmount = true },
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destination3.ScriptPubKey, SubtractFeeFromAmount = false }
                }.ToList()
            };

            Transaction transaction = walletTransactionHandler.BuildTransaction(context);
            Assert.Equal(3, transaction.Inputs.Count); // 3 inputs
            Assert.Equal(3, transaction.Outputs.Count); // 3 outputs with change
            Assert.True(transaction.Outputs.Count(i => i.Value.Satoshi < 5_000_000_000) == 2); // 2 outputs should have fees taken from the amount
        }

        [Fact]
        public void When_BuildTransactionIsCalledWithoutTransactionFee_Then_FeeIsDeductedFromSingleOutputInTransaction()
        {
            DataFolder dataFolder = CreateDataFolder(this);

            IWalletRepository walletRepository = new SQLiteWalletRepository(this.LoggerFactory.Object, dataFolder, this.Network, DateTimeProvider.Default, new ScriptAddressReader())
            {
                TestMode = true
            };

            var walletFeePolicy = new Mock<IWalletFeePolicy>();
            walletFeePolicy.Setup(w => w.GetFeeRate(FeeType.Low.ToConfirmations())).Returns(new FeeRate(20000));

            var walletManager = new WalletManager(this.LoggerFactory.Object, this.Network, new ChainIndexer(this.Network), new WalletSettings(NodeSettings.Default(this.Network)),
                dataFolder, walletFeePolicy.Object, DateTimeProvider.Default, walletRepository);

            walletManager.Start();

            var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

            var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, walletManager, walletFeePolicy.Object, this.Network, this.standardTransactionPolicy, reserveUtxoService);

            (Wallet wallet, ExtKey extKey) = WalletTestsHelpers.GenerateBlankWalletWithExtKey("myWallet1", "password", walletRepository);

            walletManager.Wallets.Add(wallet);

            int accountIndex = 0;
            ExtKey addressExtKey = extKey.Derive(new KeyPath($"m/44'/{this.Network.Consensus.CoinType}'/{accountIndex}'"));
            ExtPubKey extPubKey = addressExtKey.Neuter();

            HdAccount account = wallet.AddNewAccount(extPubKey, accountName: "account1");

            var address = account.ExternalAddresses.First();
            var destination = account.InternalAddresses.First();
            var destination2 = account.InternalAddresses.Skip(1).First();
            var destination3 = account.InternalAddresses.Skip(2).First();

            // Wallet with 4 coinbase outputs of 50 = 200.
            var chain = new ChainIndexer(wallet.Network);
            WalletTestsHelpers.AddBlocksWithCoinbaseToChain(wallet.Network, chain, address, 4);

            var walletReference = new WalletAccountReference
            {
                AccountName = "account1",
                WalletName = "myWallet1"
            };

            // Create a transaction with 3 outputs 50 + 50 + 50 = 150 but with fees charged to recipients.
            var context = new TransactionBuildContext(this.Network)
            {
                AccountReference = walletReference,
                MinConfirmations = 0,
                FeeType = FeeType.Low,
                WalletPassword = "password",
                Recipients = new[]
                {
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destination.ScriptPubKey, SubtractFeeFromAmount = true },
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destination2.ScriptPubKey, SubtractFeeFromAmount = false },
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destination3.ScriptPubKey, SubtractFeeFromAmount = false }
                }.ToList()
            };

            Transaction transaction = walletTransactionHandler.BuildTransaction(context);
            Assert.Equal(3, transaction.Inputs.Count); // 3 inputs
            Assert.Equal(3, transaction.Outputs.Count); // 3 outputs with change
            Assert.True(transaction.Outputs.Count(i => i.Value.Satoshi < 5_000_000_000) == 1); // 1 output should have fees taken from the amount
        }

        [Fact]
        public void When_BuildTransactionIsCalledWithoutTransactionFee_Then_MultipleSubtractFeeRecipients_ThrowsException()
        {
            DataFolder dataFolder = CreateDataFolder(this);

            IWalletRepository walletRepository = new SQLiteWalletRepository(this.LoggerFactory.Object, dataFolder, this.Network, DateTimeProvider.Default, new ScriptAddressReader())
            {
                TestMode = true
            };

            var walletFeePolicy = new Mock<IWalletFeePolicy>();
            walletFeePolicy.Setup(w => w.GetFeeRate(FeeType.Low.ToConfirmations())).Returns(new FeeRate(20000));

            var walletManager = new WalletManager(this.LoggerFactory.Object, this.Network, new ChainIndexer(this.Network), new WalletSettings(NodeSettings.Default(this.Network)),
                dataFolder, walletFeePolicy.Object, DateTimeProvider.Default, walletRepository);

            walletManager.Start();

            var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

            var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, walletManager, walletFeePolicy.Object, this.Network, this.standardTransactionPolicy, reserveUtxoService);

            (Wallet wallet, ExtKey extKey) = WalletTestsHelpers.GenerateBlankWalletWithExtKey("myWallet1", "password", walletRepository);

            walletManager.Wallets.Add(wallet);

            int accountIndex = 0;
            ExtKey addressExtKey = extKey.Derive(new KeyPath($"m/44'/{this.Network.Consensus.CoinType}'/{accountIndex}'"));
            ExtPubKey extPubKey = addressExtKey.Neuter();

            HdAccount account = wallet.AddNewAccount(extPubKey, accountName: "account1");

            var address = account.ExternalAddresses.First();
            var destination = account.InternalAddresses.First();
            var destination2 = account.InternalAddresses.Skip(1).First();
            var destination3 = account.InternalAddresses.Skip(2).First();

            // Wallet with 4 coinbase outputs of 50 = 200.
            var chain = new ChainIndexer(wallet.Network);
            WalletTestsHelpers.AddBlocksWithCoinbaseToChain(wallet.Network, chain, address, 4);

            var walletReference = new WalletAccountReference
            {
                AccountName = "account1",
                WalletName = "myWallet1"
            };

            // Create a transaction with 3 outputs 50 + 50 + 50 = 150 but with fees charged to recipients.
            var context = new TransactionBuildContext(this.Network)
            {
                AccountReference = walletReference,
                MinConfirmations = 0,
                FeeType = FeeType.Low,
                WalletPassword = "password",
                Recipients = new[]
                {
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destination.ScriptPubKey, SubtractFeeFromAmount = true },
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destination2.ScriptPubKey, SubtractFeeFromAmount = true },
                    new Recipient { Amount = new Money(50, MoneyUnit.BTC), ScriptPubKey = destination3.ScriptPubKey, SubtractFeeFromAmount = false }
                }.ToList()
            };

            Assert.Throws<WalletException>(() => walletTransactionHandler.BuildTransaction(context));
        }

        public static TransactionBuildContext CreateContext(Network network, WalletAccountReference accountReference, string password,
            Script destinationScript, Money amount, FeeType feeType, int minConfirmations, string opReturnData = null, List<Recipient> recipients = null)
        {
            return new TransactionBuildContext(network)
            {
                AccountReference = accountReference,
                MinConfirmations = minConfirmations,
                FeeType = feeType,
                OpReturnData = opReturnData,
                WalletPassword = password,
                Sign = !string.IsNullOrEmpty(password),
                Recipients = recipients ?? new[] { new Recipient { Amount = amount, ScriptPubKey = destinationScript } }.ToList()
            };
        }

        private WalletTransactionHandlerTestContext SetupWallet(FeeRate feeRate = null, int coinBaseBlocks = 1)
        {
            DataFolder dataFolder = CreateDataFolder(this);

            IWalletRepository walletRepository = new SQLiteWalletRepository(this.LoggerFactory.Object, dataFolder, this.Network, DateTimeProvider.Default, new ScriptAddressReader())
            {
                TestMode = true
            };

            var walletFeePolicy = new Mock<IWalletFeePolicy>();
            walletFeePolicy.Setup(w => w.GetFeeRate(FeeType.Low.ToConfirmations()))
                .Returns(feeRate ?? new FeeRate(20000));

            var chain = new ChainIndexer(this.Network);

            var walletManager = new WalletManager(this.LoggerFactory.Object, this.Network, chain,
                new WalletSettings(NodeSettings.Default(this.Network)), dataFolder,
                walletFeePolicy.Object, DateTimeProvider.Default, walletRepository);

            var reserveUtxoService = new ReserveUtxoService(this.loggerFactory, new Mock<ISignals>().Object);

            var walletTransactionHandler = new WalletTransactionHandler(this.LoggerFactory.Object, walletManager, walletFeePolicy.Object, this.Network, this.standardTransactionPolicy, reserveUtxoService);

            walletManager.Start();

            var walletReference = new WalletAccountReference
            {
                AccountName = "account1",
                WalletName = "myWallet1"
            };

            Wallet wallet = WalletTestsHelpers.GenerateBlankWallet(walletReference.WalletName, "password", walletRepository);
            (ExtKey ExtKey, string ExtPubKey) accountKeys = WalletTestsHelpers.GenerateAccountKeys(wallet, "password", $"m/44'/{this.Network.Consensus.CoinType}'/0'");

            var account = wallet.AddNewAccount(accountKeys.ExtKey.Neuter(), accountName: walletReference.AccountName);

            var destinationAddress = account.ExternalAddresses.Skip(1).First();

            (PubKey PubKey, BitcoinPubKeyAddress Address) destinationKeys = (destinationAddress.Pubkey.GetDestinationPublicKeys(this.Network).First(), new BitcoinPubKeyAddress(destinationAddress.Address, this.Network));

            HdAddress address = account.ExternalAddresses.ElementAt(0);

            TransactionData addressTransaction = null;
            if (coinBaseBlocks != 0)
            {
                WalletTestsHelpers.AddBlocksWithCoinbaseToChain(wallet.Network, chain, address, coinBaseBlocks);
                addressTransaction = address.Transactions.First();
            }

            return new WalletTransactionHandlerTestContext
            {
                Wallet = wallet,
                AccountKeys = accountKeys,
                DestinationKeys = destinationKeys,
                AddressTransaction = addressTransaction,
                WalletTransactionHandler = walletTransactionHandler,
                WalletReference = walletReference
            };
        }
    }

    /// <summary>
    /// Data carrier class for objects required to test the <see cref="WalletTransactionHandler"/>.
    /// </summary>
    public class WalletTransactionHandlerTestContext
    {
        public Wallet Wallet { get; set; }

        public (ExtKey ExtKey, string ExtPubKey) AccountKeys { get; set; }

        public (PubKey PubKey, BitcoinPubKeyAddress Address) DestinationKeys { get; set; }

        public TransactionData AddressTransaction { get; set; }

        public WalletTransactionHandler WalletTransactionHandler { get; set; }

        public WalletAccountReference WalletReference { get; set; }
    }
}