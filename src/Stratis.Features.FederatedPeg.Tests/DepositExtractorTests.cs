using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NBitcoin;
using NSubstitute;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Networks;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Features.FederatedPeg.Tests.Utils;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    public class DepositExtractorTests
    {
        public const string TargetETHAddress = "0x4F26FfBe5F04ED43630fdC30A87638d53D0b0876";

        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IFederatedPegSettings federationSettings;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly DepositExtractor depositExtractor;
        private readonly Network network;
        private readonly MultisigAddressHelper addressHelper;
        private readonly TestTransactionBuilder transactionBuilder;
        private readonly RetrievalTypeConfirmations retrievalTypeConfirmations;

        public DepositExtractorTests()
        {
            this.network = new CirrusRegTest();

            this.addressHelper = new MultisigAddressHelper(this.network, new StraxRegTest());

            this.conversionRequestRepository = Substitute.For<IConversionRequestRepository>();

            this.federationSettings = Substitute.For<IFederatedPegSettings>();
            this.federationSettings.IsMainChain.Returns(true);
            this.federationSettings.SmallDepositThresholdAmount.Returns(Money.Coins(10));
            this.federationSettings.NormalDepositThresholdAmount.Returns(Money.Coins(20));
            this.federationSettings.MultiSigRedeemScript.Returns(this.addressHelper.PayToMultiSig);

            this.opReturnDataReader = Substitute.For<IOpReturnDataReader>();
            this.opReturnDataReader.TryGetTargetAddress(null, out string address).Returns(callInfo => { callInfo[1] = null; return false; });

            IExternalApiClient externalClient = Substitute.For<IExternalApiClient>();
            externalClient.EstimateConversionTransactionFeeAsync().Returns("1.0");
            this.depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federationSettings, this.network, this.opReturnDataReader);
            this.transactionBuilder = new TestTransactionBuilder();

            this.retrievalTypeConfirmations = new RetrievalTypeConfirmations(this.network, new NodeDeployments(this.network, new ChainIndexer(this.network)), this.federationSettings);
        }

        // Normal Deposits
        [Fact]
        public async Task ExtractNormalDeposits_Should_Only_Find_Deposits_To_Multisig()
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
            IReadOnlyList<IDeposit> extractedDeposits = await this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, this.retrievalTypeConfirmations);

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
        public async Task ExtractNormalDeposits_ShouldCreate_OneDepositPerTransaction_ToMultisig()
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
            IReadOnlyList<IDeposit> extractedDeposits = await this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, this.retrievalTypeConfirmations);

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
        public async Task ExtractNormalDeposits_ReturnDeposits_AboveFasterThresholdAsync()
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
            IReadOnlyList<IDeposit> extractedDeposits = await this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, this.retrievalTypeConfirmations);

            IEnumerable<IDeposit> applicable = extractedDeposits.Where(d => d.Amount >= this.federationSettings.SmallDepositThresholdAmount);
            applicable.Count().Should().Be(2);
            foreach (IDeposit extractedDeposit in applicable)
            {
                Assert.True(extractedDeposit.Amount >= this.federationSettings.SmallDepositThresholdAmount);
            }
        }

        [Fact]
        public async Task ExtractNormalConversionDeposits_ReturnDeposits_AboveFasterThresholdAsync()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            BitcoinPubKeyAddress targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            byte[] opReturnBytes = Encoding.UTF8.GetBytes(targetAddress.ToString());

            // Set amount to be less than the small threshold amount.
            CreateDepositTransaction(targetAddress, block, this.federationSettings.SmallDepositThresholdAmount - 1, opReturnBytes);

            byte[] ethOpReturnBytes = Encoding.UTF8.GetBytes(InterFluxOpReturnEncoder.Encode(DestinationChain.ETH, TargetETHAddress));

            // Set amount to be exactly the small threshold amount.
            CreateConversionTransaction(block, DepositValidationHelper.ConversionTransactionMinimum - 1, ethOpReturnBytes);

            // Set amount to be greater than the small threshold amount.
            CreateConversionTransaction(block, DepositValidationHelper.ConversionTransactionMinimum + 1, ethOpReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = await this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, this.retrievalTypeConfirmations);

            // Should only be 1, with the value just over the withdrawal fee.
            IEnumerable<IDeposit> applicable = extractedDeposits.Where(d => d.Amount > DepositValidationHelper.ConversionTransactionMinimum && d.RetrievalType == DepositRetrievalType.ConversionLarge);
            applicable.Count().Should().Be(1);
            foreach (IDeposit extractedDeposit in applicable)
            {
                Assert.True(extractedDeposit.Amount >= this.federationSettings.SmallDepositThresholdAmount);
            }
        }

        // Normal Deposits
        [Fact]
        public async Task ExtractNormalDeposits_ReturnDeposits_AboveSmallThreshold_BelowEqualToNormalThresholdAsync()
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
            IReadOnlyList<IDeposit> extractedDeposits = await this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, this.retrievalTypeConfirmations);

            IEnumerable<IDeposit> applicable = extractedDeposits.Where(d => d.RetrievalType == DepositRetrievalType.Normal);

            // Should be 2, with the value just over the withdrawal fee.
            applicable.Count().Should().Be(2);
            foreach (IDeposit extractedDeposit in applicable)
            {
                Assert.True(extractedDeposit.Amount >= this.federationSettings.SmallDepositThresholdAmount);
            }
        }

        // Small Deposits
        [Fact]
        public async Task ExtractSmallDeposits_ReturnDeposits_BelowSmallThreshold_AboveMinimumAsync()
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
            IReadOnlyList<IDeposit> extractedDeposits = await this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, this.retrievalTypeConfirmations);

            IEnumerable<IDeposit> applicable = extractedDeposits.Where(d => d.RetrievalType == DepositRetrievalType.Small);

            // Should only be two, with the value just over the withdrawal fee.
            applicable.Count().Should().Be(2);
            foreach (IDeposit extractedDeposit in applicable)
            {
                Assert.True(extractedDeposit.Amount <= this.federationSettings.SmallDepositThresholdAmount);
            }
        }

        [Fact]
        public async Task ExtractConversionDeposits_BelowAndAboveThresholdAsync()
        {
            Block block = this.network.Consensus.ConsensusFactory.CreateBlock();

            byte[] ethOpReturnBytes = Encoding.UTF8.GetBytes(InterFluxOpReturnEncoder.Encode(DestinationChain.ETH, TargetETHAddress));

            CreateConversionTransaction(block, DepositValidationHelper.ConversionTransactionMinimum - 1, ethOpReturnBytes);

            CreateConversionTransaction(block, DepositValidationHelper.ConversionTransactionMinimum + 1, ethOpReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = await this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, this.retrievalTypeConfirmations);

            // Should only be two, with the value just over the withdrawal fee.
            extractedDeposits.Count.Should().Be(1);
            foreach (IDeposit extractedDeposit in extractedDeposits)
            {
                Assert.True(extractedDeposit.Amount == DepositValidationHelper.ConversionTransactionMinimum + 1);
            }
        }

        // Large Deposits
        [Fact]
        public async Task ExtractLargeDeposits_ReturnDeposits_AboveNormalThresholdAsync()
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
            IReadOnlyList<IDeposit> extractedDeposits = await this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, this.retrievalTypeConfirmations);

            // Should only be 1, with the value just over the withdrawal fee.
            IEnumerable<IDeposit> applicable = extractedDeposits.Where(d => d.Amount > this.federationSettings.NormalDepositThresholdAmount);
            applicable.Count().Should().Be(1);
            foreach (IDeposit extractedDeposit in applicable)
            {
                Assert.True(extractedDeposit.Amount > this.federationSettings.NormalDepositThresholdAmount);
            }
        }

        // Conversion deposits
        [Fact]
        public async Task ExtractLargeConversionDeposits_ReturnDeposits_AboveNormalThresholdAsync()
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

            byte[] ethOpReturnBytes = Encoding.UTF8.GetBytes(InterFluxOpReturnEncoder.Encode(DestinationChain.ETH, TargetETHAddress));

            // Set amount to be equal to the normal threshold amount.
            CreateConversionTransaction(block, DepositValidationHelper.ConversionTransactionMinimum - 1, ethOpReturnBytes);

            // Set amount to be greater than the conversion deposit minimum amount.
            CreateConversionTransaction(block, DepositValidationHelper.ConversionTransactionMinimum + 1, ethOpReturnBytes);

            int blockHeight = 12345;
            IReadOnlyList<IDeposit> extractedDeposits = await this.depositExtractor.ExtractDepositsFromBlock(block, blockHeight, this.retrievalTypeConfirmations);

            // Should only be 1, with the value just over the withdrawal fee.
            IEnumerable<IDeposit> applicable = extractedDeposits.Where(d => d.RetrievalType == DepositRetrievalType.ConversionLarge);
            applicable.Count().Should().Be(1);
            foreach (IDeposit extractedDeposit in applicable)
            {
                Assert.True(extractedDeposit.Amount > DepositValidationHelper.ConversionTransactionMinimum);
                Assert.Equal(TargetETHAddress, extractedDeposit.TargetAddress);
            }
        }

        private Transaction CreateConversionTransaction(Block block, Money depositAmount, byte[] opReturnBytes)
        {
            // Create the conversion transaction.
            Transaction conversionTransaction = this.transactionBuilder.BuildOpReturnTransaction(this.addressHelper.SourceChainMultisigAddress, opReturnBytes, depositAmount);

            // Add the conversion transaction to the block.
            block.AddTransaction(conversionTransaction);

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