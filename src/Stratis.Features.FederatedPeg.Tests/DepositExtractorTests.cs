using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NSubstitute;
using Stratis.Bitcoin.Networks;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Features.FederatedPeg.Tests.Utils;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    public class DepositExtractorTests
    {
        private readonly IFederatedPegSettings federationSettings;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly DepositExtractor depositExtractor;
        private readonly Network network;
        private readonly MultisigAddressHelper addressHelper;
        private readonly TestTransactionBuilder transactionBuilder;

        public DepositExtractorTests()
        {
            this.network = CirrusNetwork.NetworksSelector.Regtest();

            ILoggerFactory loggerFactory = Substitute.For<ILoggerFactory>();

            this.addressHelper = new MultisigAddressHelper(this.network, Networks.Strax.Regtest());

            this.federationSettings = Substitute.For<IFederatedPegSettings>();
            this.federationSettings.FasterDepositThresholdAmount.Returns(Money.Coins(10));
            this.federationSettings.MultiSigRedeemScript.Returns(this.addressHelper.PayToMultiSig);
            this.federationSettings.GetWithdrawalTransactionFee(Arg.Any<int>()).ReturnsForAnyArgs((x) =>
            {
                int numInputs = x.ArgAt<int>(0);

                return FederatedPegSettings.BaseTransactionFee + FederatedPegSettings.InputTransactionFee * numInputs;
            });

            this.opReturnDataReader = Substitute.For<IOpReturnDataReader>();
            this.opReturnDataReader.TryGetTargetAddress(null, out string address).Returns(callInfo => { callInfo[1] = null; return false; });

            this.depositExtractor = new DepositExtractor(loggerFactory, this.federationSettings, this.opReturnDataReader);
            this.transactionBuilder = new TestTransactionBuilder();
        }

        // Normal Deposits
        [Fact]
        public void ExtractNormalDeposits_Should_Only_Find_Deposits_To_Multisig()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            Transaction depositTransaction = CreateDepositTransaction(targetAddress, block, Money.Coins(11), opReturnBytes);

            Transaction nonDepositTransactionToMultisig = this.transactionBuilder.BuildTransaction(this.addressHelper.SourceChainMultisigAddress);
            block.AddTransaction(nonDepositTransactionToMultisig);

            BitcoinPubKeyAddress otherAddress = this.addressHelper.GetNewSourceChainPubKeyAddress();
            otherAddress.ToString().Should().NotBe(this.addressHelper.SourceChainMultisigAddress.ToString(), "otherwise the next deposit should actually be extracted");
            Transaction depositTransactionToOtherAddress = this.transactionBuilder.BuildOpReturnTransaction(otherAddress, opReturnBytes);
            block.AddTransaction(depositTransactionToOtherAddress);

            Transaction nonDepositTransactionToOtherAddress = this.transactionBuilder.BuildTransaction(otherAddress);
            block.AddTransaction(nonDepositTransactionToOtherAddress);

            int blockHeight = 230;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, DepositRetrievalType.Normal);

            extractedDeposits.Count.Should().Be(1);
            IDeposit extractedTransaction = extractedDeposits[0];

            extractedTransaction.Amount.Satoshi.Should().Be(Money.Coins(11));
            extractedTransaction.Id.Should().Be(depositTransaction.GetHash());
            extractedTransaction.TargetAddress.Should().Be(targetAddress.ToString());
            extractedTransaction.BlockNumber.Should().Be(blockHeight);
            extractedTransaction.BlockHash.Should().Be(block.GetHash());
        }

        // Normal Deposits
        [Fact]
        public void ExtractNormalDeposits_ShouldCreate_OneDepositPerTransaction_ToMultisig()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            Transaction depositTransaction = CreateDepositTransaction(targetAddress, block, Money.Coins(11), opReturnBytes);

            //add another deposit to the same address
            Transaction secondDepositTransaction = CreateDepositTransaction(targetAddress, block, Money.Coins(12), opReturnBytes);

            //add another deposit to a different address
            BitcoinPubKeyAddress newTargetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] newOpReturnBytes = Encoding.UTF8.GetBytes(newTargetAddress.ToString());
            Transaction thirdDepositTransaction = CreateDepositTransaction(newTargetAddress, block, Money.Coins(34), newOpReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, DepositRetrievalType.Normal);

            extractedDeposits.Count.Should().Be(3);
            extractedDeposits.Select(d => d.BlockNumber).Should().AllBeEquivalentTo(blockHeight);
            extractedDeposits.Select(d => d.BlockHash).Should().AllBeEquivalentTo(block.GetHash());

            IDeposit extractedTransaction = extractedDeposits[0];
            extractedTransaction.Amount.Satoshi.Should().Be(Money.Coins(11));
            extractedTransaction.Id.Should().Be(depositTransaction.GetHash());
            extractedTransaction.TargetAddress.Should()
                .Be(targetAddress.ToString());

            extractedTransaction = extractedDeposits[1];
            extractedTransaction.Amount.Satoshi.Should().Be(Money.Coins(12));
            extractedTransaction.Id.Should().Be(secondDepositTransaction.GetHash());
            extractedTransaction.TargetAddress.Should()
                .Be(targetAddress.ToString());

            extractedTransaction = extractedDeposits[2];
            extractedTransaction.Amount.Satoshi.Should().Be(Money.Coins(34));
            extractedTransaction.Id.Should().Be(thirdDepositTransaction.GetHash());
            extractedTransaction.TargetAddress.Should().Be(newTargetAddress.ToString());
        }

        // Normal Deposits
        [Fact]
        public void ExtractNormalDeposits_ReturnDeposits_AboveFasterThreshold()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Set amount to be less than the faster threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.FasterDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the faster threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.FasterDepositThresholdAmount, opReturnBytes);

            // Set amount to be greater than the faster threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.FasterDepositThresholdAmount + 1, opReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, DepositRetrievalType.Normal);

            // Should only be two, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(1);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount >= this.federationSettings.FasterDepositThresholdAmount);
            }
        }

        // Faster Deposits
        [Fact]
        public void ExtractFasterDeposits_ReturnDeposits_BelowFasterThreshold_AboveMinimum()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            // Create the target address.
            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Set amount to be less than deposit minimum
            CreateDepositTransaction(targetAddress, block, FederatedPegSettings.CrossChainTransferMinimum - 1, opReturnBytes);

            // Set amount to be less than the faster threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.FasterDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the faster threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.FasterDepositThresholdAmount, opReturnBytes);

            // Set amount to be greater than the faster threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.FasterDepositThresholdAmount + 1, opReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, DepositRetrievalType.Faster);

            // Should only be two, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(2);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount <= this.federationSettings.FasterDepositThresholdAmount);
            }
        }

        private Transaction CreateDepositTransaction(BitcoinPubKeyAddress targetAddress, Block block, Money depositAmount, byte[] opReturnBytes)
        {
            // Create the deposit transaction.
            Transaction depositTransaction = this.transactionBuilder.BuildOpReturnTransaction(this.addressHelper.SourceChainMultisigAddress, opReturnBytes, depositAmount);

            // Add the deposit transaction to the block.
            block.AddTransaction(depositTransaction);

            this.opReturnDataReader.TryGetTargetAddress(depositTransaction, out string _).Returns(callInfo =>
            {
                callInfo[1] = targetAddress.ToString();
                return true;
            });

            return depositTransaction;
        }
    }
}