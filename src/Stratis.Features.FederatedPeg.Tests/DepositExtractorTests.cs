using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentAssertions;
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
        public const string TargetEthereumAddress = "0x4F26FfBe5F04ED43630fdC30A87638d53D0b0876";

        private readonly IFederatedPegSettings federationSettings;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly DepositExtractor depositExtractor;
        private readonly Network network;
        private readonly MultisigAddressHelper addressHelper;
        private readonly TestTransactionBuilder transactionBuilder;

        public DepositExtractorTests()
        {
            this.network = new CirrusRegTest();

            this.addressHelper = new MultisigAddressHelper(this.network, new StraxRegTest());

            this.federationSettings = Substitute.For<IFederatedPegSettings>();
            this.federationSettings.SmallDepositThresholdAmount.Returns(Money.Coins(10));
            this.federationSettings.NormalDepositThresholdAmount.Returns(Money.Coins(20));

            this.federationSettings.MultiSigRedeemScript.Returns(this.addressHelper.PayToMultiSig);
            this.federationSettings.GetWithdrawalTransactionFee(Arg.Any<int>()).ReturnsForAnyArgs((x) =>
            {
                int numInputs = x.ArgAt<int>(0);
                return FederatedPegSettings.BaseTransactionFee + FederatedPegSettings.InputTransactionFee * numInputs;
            });

            this.opReturnDataReader = Substitute.For<IOpReturnDataReader>();
            this.opReturnDataReader.TryGetTargetAddress(null, out string address).Returns(callInfo => { callInfo[1] = null; return false; });

            this.depositExtractor = new DepositExtractor(this.federationSettings, this.network, this.opReturnDataReader);
            this.transactionBuilder = new TestTransactionBuilder();
        }

        // Normal Deposits
        [Fact]
        public void ExtractNormalDeposits_Should_Only_Find_Deposits_To_Multisig()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Create a deposit above the small deposit threshold.
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
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, new[] { DepositRetrievalType.Normal });

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

            // Create a "normal" deposit.
            Transaction depositTransaction = CreateDepositTransaction(targetAddress, block, Money.Coins(11), opReturnBytes);

            // Create another "normal" deposit to the same address.
            Transaction secondDepositTransaction = CreateDepositTransaction(targetAddress, block, Money.Coins(12), opReturnBytes);

            // Create another "normal" deposit to a different address.
            BitcoinPubKeyAddress newTargetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] newOpReturnBytes = Encoding.UTF8.GetBytes(newTargetAddress.ToString());
            Transaction thirdDepositTransaction = CreateDepositTransaction(newTargetAddress, block, Money.Coins(12), newOpReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, new[] { DepositRetrievalType.Normal });

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
            extractedTransaction.Amount.Satoshi.Should().Be(Money.Coins(12));
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

            // Set amount to be less than the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount, opReturnBytes);

            // Set amount to be greater than the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount + 1, opReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, new[] { DepositRetrievalType.Normal });

            // Should only be 1, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(1);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount >= this.federationSettings.SmallDepositThresholdAmount);
            }
        }

        [Fact]
        public void ExtractNormalConversionDeposits_ReturnDeposits_AboveFasterThreshold()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Set amount to be less than the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the small threshold amount.
            CreateConversionTransaction(TargetEthereumAddress, block, this.federationSettings.SmallDepositThresholdAmount, opReturnBytes);

            // Set amount to be greater than the small threshold amount.
            CreateConversionTransaction(TargetEthereumAddress, block, this.federationSettings.SmallDepositThresholdAmount + 1, opReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, new[] { DepositRetrievalType.ConversionNormal });

            // Should only be 1, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(1);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount >= this.federationSettings.SmallDepositThresholdAmount);
            }
        }

        // Normal Deposits
        [Fact]
        public void ExtractNormalDeposits_ReturnDeposits_AboveSmallThreshold_BelowEqualToNormalThreshold()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Set amount to be less than the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount, opReturnBytes);

            // Set amount to be less than the normal threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.NormalDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the normal threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.NormalDepositThresholdAmount, opReturnBytes);

            // Set amount to be greater than the normal threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.NormalDepositThresholdAmount + 1, opReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, new[] { DepositRetrievalType.Normal });

            // Should be 2, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(2);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount >= this.federationSettings.SmallDepositThresholdAmount);
            }
        }

        // Small Deposits
        [Fact]
        public void ExtractSmallDeposits_ReturnDeposits_BelowSmallThreshold_AboveMinimum()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            // Create the target address.
            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Set amount to be less than deposit minimum
            CreateDepositTransaction(targetAddress, block, FederatedPegSettings.CrossChainTransferMinimum - 1, opReturnBytes);

            // Set amount to be less than the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount, opReturnBytes);

            // Set amount to be greater than the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount + 1, opReturnBytes);

            // Set amount to be greater than the normal threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.NormalDepositThresholdAmount + 1, opReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, new[] { DepositRetrievalType.Small });

            // Should only be two, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(2);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount <= this.federationSettings.SmallDepositThresholdAmount);
            }
        }

        [Fact]
        public void ExtractSmallConversionDeposits_ReturnDeposits_BelowSmallThreshold_AboveMinimum()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            // Create the target address.
            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Set amount to be less than deposit minimum
            CreateConversionTransaction(TargetEthereumAddress, block, FederatedPegSettings.CrossChainTransferMinimum - 1, opReturnBytes);

            // Set amount to be less than the small threshold amount.
            CreateConversionTransaction(TargetEthereumAddress, block, this.federationSettings.SmallDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the small threshold amount.
            CreateConversionTransaction(TargetEthereumAddress, block, this.federationSettings.SmallDepositThresholdAmount, opReturnBytes);

            // Set amount to be greater than the small threshold amount.
            CreateConversionTransaction(TargetEthereumAddress, block, this.federationSettings.SmallDepositThresholdAmount + 1, opReturnBytes);

            // Set amount to be greater than the normal threshold amount.
            CreateConversionTransaction(TargetEthereumAddress, block, this.federationSettings.NormalDepositThresholdAmount + 1, opReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, new[] { DepositRetrievalType.ConversionSmall });

            // Should only be two, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(2);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount <= this.federationSettings.SmallDepositThresholdAmount);
            }
        }

        // Large Deposits
        [Fact]
        public void ExtractLargeDeposits_ReturnDeposits_AboveNormalThreshold()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            // Create the target address.
            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Set amount to be less than deposit minimum
            CreateDepositTransaction(targetAddress, block, FederatedPegSettings.CrossChainTransferMinimum - 1, opReturnBytes);

            // Set amount to be less than the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the normal threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.NormalDepositThresholdAmount, opReturnBytes);

            // Set amount to be equal to the normal threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.NormalDepositThresholdAmount, opReturnBytes);

            // Set amount to be greater than the normal threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.NormalDepositThresholdAmount + 1, opReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, new[] { DepositRetrievalType.Large });

            // Should only be 1, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(1);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount > this.federationSettings.NormalDepositThresholdAmount);
            }
        }

        // Conversion deposits
        [Fact]
        public void ExtractLargeConversionDeposits_ReturnDeposits_AboveNormalThreshold()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            // Create the target address.
            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Set amount to be less than deposit minimum
            CreateDepositTransaction(targetAddress, block, FederatedPegSettings.CrossChainTransferMinimum - 1, opReturnBytes);

            // Set amount to be less than the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount - 1, opReturnBytes);

            // Set amount to be exactly the normal threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.NormalDepositThresholdAmount, opReturnBytes);

            // Set amount to be equal to the normal threshold amount.
            CreateConversionTransaction(TargetEthereumAddress, block, this.federationSettings.NormalDepositThresholdAmount, opReturnBytes);

            // Set amount to be greater than the normal threshold amount.
            CreateConversionTransaction(TargetEthereumAddress, block, this.federationSettings.NormalDepositThresholdAmount + 1, opReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, new[] { DepositRetrievalType.ConversionLarge });

            // Should only be 1, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(1);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount > this.federationSettings.NormalDepositThresholdAmount);
                Assert.Equal(TargetEthereumAddress, extractedDeposit.TargetAddress);
            }
        }

        private Transaction CreateConversionTransaction(string targetEthereumAddress, Block block, Money depositAmount, byte[] opReturnBytes)
        {
            // Create the conversion transaction.
            Transaction conversionTransaction = this.transactionBuilder.BuildOpReturnTransaction(this.addressHelper.SourceChainMultisigAddress, opReturnBytes, depositAmount);

            // Add the conversion transaction to the block.
            block.AddTransaction(conversionTransaction);

            this.opReturnDataReader.TryGetTargetEthereumAddress(conversionTransaction, out string _).Returns(callInfo =>
            {
                callInfo[1] = targetEthereumAddress;
                return true;
            });

            return conversionTransaction;
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