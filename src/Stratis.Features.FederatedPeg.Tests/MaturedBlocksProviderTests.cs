using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NSubstitute;
using NSubstitute.Core;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Conversion;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.Models;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Features.FederatedPeg.Tests.Utils;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    public sealed class MaturedBlocksProviderTests
    {
        private readonly MultisigAddressHelper addressHelper;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IConsensusManager consensusManager;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger logger;
        private readonly Network mainChainNetwork;
        private readonly Network network;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly TestTransactionBuilder transactionBuilder;
        private readonly byte[] opReturnBytes;
        private readonly BitcoinPubKeyAddress targetAddress;
        private List<ChainedHeaderBlock> blocks;

        public MaturedBlocksProviderTests()
        {
            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.logger = Substitute.For<ILogger>();
            this.loggerFactory.CreateLogger(null).ReturnsForAnyArgs(this.logger);
            this.conversionRequestRepository = Substitute.For<IConversionRequestRepository>();
            this.consensusManager = Substitute.For<IConsensusManager>();
            this.network = new CirrusRegTest();
            this.mainChainNetwork = new StraxRegTest();

            this.opReturnDataReader = Substitute.For<IOpReturnDataReader>();
            this.opReturnDataReader.TryGetTargetAddress(null, out string address).Returns(callInfo => { callInfo[1] = null; return false; });

            this.transactionBuilder = new TestTransactionBuilder();

            this.addressHelper = new MultisigAddressHelper(this.network, this.mainChainNetwork);
            this.targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            this.opReturnBytes = Encoding.UTF8.GetBytes(InterFluxOpReturnEncoder.Encode(DestinationChain.STRAX, this.targetAddress.ToString()));

            this.federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            this.federatedPegSettings.MultiSigRedeemScript.Returns(this.addressHelper.PayToMultiSig);

            this.federatedPegSettings.MinimumConfirmationsSmallDeposits.Returns(5);
            this.federatedPegSettings.MinimumConfirmationsNormalDeposits.Returns(10);
            this.federatedPegSettings.MinimumConfirmationsLargeDeposits.Returns(20);

            this.federatedPegSettings.SmallDepositThresholdAmount.Returns(Money.Coins(10));
            this.federatedPegSettings.NormalDepositThresholdAmount.Returns(Money.Coins(100));

            this.consensusManager.GetBlocksAfterBlock(Arg.Any<ChainedHeader>(), MaturedBlocksProvider.MaturedBlocksBatchSize, Arg.Any<CancellationTokenSource>()).Returns(delegate (CallInfo info)
            {
                var chainedHeader = (ChainedHeader)info[0];
                if (chainedHeader == null)
                    return this.blocks.Where(x => x.ChainedHeader.Height <= this.consensusManager.Tip.Height).ToArray();

                return this.blocks.SkipWhile(x => x.ChainedHeader.Height <= chainedHeader.Height).Where(x => x.ChainedHeader.Height <= this.consensusManager.Tip.Height).ToArray();
            });
        }

        [Fact]
        public async Task GetMaturedBlocksReturnsDepositsAsync()
        {
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(10, true, this.mainChainNetwork);

            ChainedHeader tip = this.blocks.Last().ChainedHeader;

            IFederatedPegSettings federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            federatedPegSettings.MinimumConfirmationsNormalDeposits.Returns(0);

            var deposits = new List<IDeposit>() { new Deposit(new uint256(0), DepositRetrievalType.Normal, 100, "test", DestinationChain.STRAX, 0, new uint256(1)) };

            // Set the first block up to return 100 normal deposits.
            IDepositExtractor depositExtractor = Substitute.For<IDepositExtractor>();
            depositExtractor.ExtractDepositsFromBlock(this.blocks.First().Block, this.blocks.First().ChainedHeader.Height, new[] { DepositRetrievalType.Normal }).ReturnsForAnyArgs(deposits);
            this.consensusManager.Tip.Returns(tip);

            // Makes every block a matured block.
            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, federatedPegSettings);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.RetrieveDepositsAsync(0);

            Assert.Equal(11, depositsResult.Value.Count);
        }

        /// <summary>
        /// Scenario 1
        /// 
        /// Tip                     = 20
        /// Small Deposits from     = 5 to 9
        /// Normal Deposits from    = 11 to 17
        /// Small Min Confirms      = 5
        /// Normal Min Confirms     = 10
        /// Retrieve from           = 5
        /// 
        /// Returns 0 normal deposits
        /// Returns 4 faster deposits
        /// </summary>
        [Fact]
        public async Task RetrieveDeposits_ReturnsDataToAdvanceNextMaturedBlockHeightAsync()
        {
            // Create a "chain" of 20 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(20, true, this.mainChainNetwork);

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i < 17; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 4 faster deposits to blocks 5 through to 9 (the amounts are less than 10).
            for (int i = 5; i < 9; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => this.blocks.Single(x => x.ChainedHeader.HashBlock == hash && x.ChainedHeader.Height <= this.consensusManager.Tip.Height)).ToArray();
            });

            var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);
            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings);

            int nextMaturedBlockHeight = 1;
            for (int i = 1; i < this.blocks.Count; i++)
            {
                this.consensusManager.Tip.Returns(this.blocks[i].ChainedHeader);

                SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.RetrieveDepositsAsync(nextMaturedBlockHeight);

                if (depositsResult?.Value != null && nextMaturedBlockHeight == depositsResult.Value.Min(b => b.BlockInfo.BlockHeight))
                {
                    nextMaturedBlockHeight = depositsResult.Value.Max(b => b.BlockInfo.BlockHeight) + 1;
                }
            }

            // Test whether the returned data is able to advance the NextMaturedBlockHeight.
            Assert.Equal(21, nextMaturedBlockHeight);
        }

        /// <summary>
        /// Scenario 2
        /// 
        /// Tip                     = 30
        /// Small Deposits from     = 5 to 9
        /// Normal Deposits from    = 11 to 17
        /// Small Min Confirms      = 5
        /// Normal Min Confirms     = 10
        /// Retrieve from heigt     = 5
        /// 
        /// Returns 6 normal deposits
        /// Returns 4 faster deposits
        /// </summary>
        [Fact]
        public async Task RetrieveDeposits_ReturnsSmallAndNormalDeposits_Scenario2()
        {
            // Create a "chain" of 30 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, true, this.mainChainNetwork);

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i < 17; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 4 small deposits to blocks 5 through to 9 (the amounts are less than 10).
            for (int i = 5; i < 9; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => this.blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(this.blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);
            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.RetrieveDepositsAsync(5);

            // Total deposits
            Assert.Equal(10, depositsResult.Value.SelectMany(b => b.Deposits).Count());

            // Normal Deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Normal).Count());

            // Small Deposits
            Assert.Equal(4, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Small).Count());
        }

        /// <summary>
        /// Scenario 3 (Overlaps)
        /// 
        /// Tip                     = 30
        /// 
        /// Small Deposits from     = 8 to 13
        /// Normal Deposits from    = 11 to 15
        /// 
        /// Small Min Confirms      = 5
        /// Normal Min Confirms     = 10
        /// 
        /// Retrieve from heigt     = 5
        /// 
        /// Returns 6 faster deposits
        /// Returns 5 normal deposits
        /// </summary>
        [Fact]
        public async Task RetrieveDeposits_ReturnsSmallAndNormalDeposits_Scenario3()
        {
            // Create a "chain" of 30 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, true, this.mainChainNetwork);

            // Add 6 small deposits to blocks 8 through to 13 (the amounts are less than 10).
            for (int i = 8; i <= 13; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(8), this.opReturnBytes);
            }

            // Add 5 normal deposits to block 11 through to 15.
            for (int i = 11; i <= 15; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.Tip.Returns(this.blocks.Last().ChainedHeader);
            var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);

            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.RetrieveDepositsAsync(5);

            // Total deposits
            Assert.Equal(11, depositsResult.Value.SelectMany(b => b.Deposits).Count());

            // Small Deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Small).Count());

            // Normal Deposits
            Assert.Equal(5, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Normal).Count());
        }

        /// <summary>
        /// Scenario 4
        /// 
        /// Tip                     = 30
        /// Small Deposits from     = 5 to 9
        /// Normal Deposits from    = 11 to 17
        /// Small Min Confirms      = 5
        /// Normal Min Confirms     = 10
        /// Retrieve from           = 10
        /// 
        /// Returns 6 normal deposits
        /// Returns 0 small deposits
        /// </summary>
        [Fact]
        public async Task RetrieveDeposits_ReturnsSmallAndNormalDeposits_Scenario4()
        {
            // Create a "chain" of 20 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, true, this.mainChainNetwork);

            // Add 4 small deposits to blocks 5 through to 8 (the amounts are less than 10).
            for (int i = 5; i <= 8; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i <= 16; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => this.blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(this.blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);
            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.RetrieveDepositsAsync(10);

            // Total deposits
            Assert.Equal(10, depositsResult.Value.SelectMany(b => b.Deposits).Count());

            // Small Deposits
            Assert.Equal(4, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Small).Count());

            // Normal Deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Normal).Count());
        }

        /// <summary>
        /// Scenario 5
        /// 
        /// Tip                     = 20
        /// Small Deposits from     = 5 to 8
        /// Normal Deposits from    = 11 to 16
        /// Small Min Confirms      = 5
        /// Normal Min Confirms     = 10
        /// 
        /// Retrieve from           = 10
        /// 
        /// Returns 0 normal deposits
        /// Returns 0 small deposits
        /// </summary>
        [Fact]
        public async Task RetrieveDeposits_ReturnsSmallAndNormalDeposits_Scenario5()
        {
            // Create a "chain" of 20 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(20, true, this.mainChainNetwork);

            // Add 4 small deposits to blocks 5 through to 8 (the amounts are less than 10).
            for (int i = 5; i <= 8; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i <= 16; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => this.blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(this.blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);
            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.RetrieveDepositsAsync(10);

            // Total deposits
            Assert.Equal(4, depositsResult.Value.SelectMany(b => b.Deposits).Count());
        }

        /// <summary>
        /// Scenario 6
        /// 
        /// Tip                     = 20
        /// Small Deposits from     = 5 to 8
        /// Normal Deposits from    = 11 to 16
        /// Large Deposits from     = 11 to 16
        /// 
        /// Small Min Confirms      = 5
        /// Normal Min Confirms     = 10
        /// Large Min Confirms      = 20
        /// 
        /// Retrieve from           = 10
        /// 
        /// Returns 0 small deposits
        /// Returns 0 normal deposits
        /// Returns 0 large deposits
        /// </summary>
        [Fact]
        public async Task RetrieveDeposits_ReturnsLargeDeposits_Scenario6()
        {
            // Create a "chain" of 20 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(20, true, this.mainChainNetwork);

            // Add 4 small deposits to blocks 5 through to 8 (the amounts are less than 10).
            for (int i = 5; i <= 8; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i <= 16; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 6 large deposits to block 11 through to 16.
            for (int i = 11; i <= 16; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins((long)this.federatedPegSettings.NormalDepositThresholdAmount + 1), this.opReturnBytes);
            }

            this.consensusManager.Tip.Returns(this.blocks.Last().ChainedHeader);
            var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);
            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.RetrieveDepositsAsync(10);

            // Total deposits
            Assert.Equal(4, depositsResult.Value.SelectMany(b => b.Deposits).Count());
        }

        /// <summary>
        /// Scenario 7
        /// 
        /// Tip                     = 40
        /// Small Deposits from     = 5 to 8
        /// Normal Deposits from    = 11 to 16
        /// Large Deposits from     = 11 to 16
        /// 
        /// Small Min Confirms      = 5
        /// Normal Min Confirms     = 10
        /// Large Min Confirms      = 20
        /// 
        /// Retrieve from           = 10
        /// 
        /// Returns 0 small deposits
        /// Returns 6 normal deposits
        /// Returns 6 large deposits
        /// </summary>
        [Fact]
        public async Task RetrieveDeposits_ReturnsLargeDeposits_Scenario7()
        {
            // Create a "chain" of 40 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(40, true, this.mainChainNetwork);

            // Add 4 small deposits to blocks 5 through to 8 (the amounts are less than 10).
            for (int i = 5; i <= 8; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i <= 16; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 6 large deposits to block 11 through to 16.
            for (int i = 11; i <= 16; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins((long)this.federatedPegSettings.NormalDepositThresholdAmount + 1), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => this.blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(this.blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);
            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.RetrieveDepositsAsync(10);

            // Total deposits
            Assert.Equal(16, depositsResult.Value.SelectMany(b => b.Deposits).Count());

            // Small Deposits
            Assert.Equal(4, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Small).Count());

            // Normal Deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Normal).Count());

            // Large Deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Large).Count());
        }

        /// <summary>
        /// Scenario 8
        /// 
        /// Tip                     = 40
        /// Normal Deposits from    = 11 to 16
        /// Large Deposits from     = 14 to 23
        /// 
        /// Small Min Confirms      = 5
        /// Normal Min Confirms     = 10
        /// Large Min Confirms      = 20
        /// 
        /// Retrieve from           = 10
        /// 
        /// Returns 0 small deposits
        /// Returns 6 normal deposits
        /// Returns 7 large deposits
        /// </summary>
        [Fact]
        public async Task RetrieveDeposits_ReturnsLargeDeposits_Scenario8()
        {
            // Create a "chain" of 40 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(40, true, this.mainChainNetwork);

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i <= 16; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 10 large deposits to block 14 through to 23.
            for (int i = 14; i <= 23; i++)
            {
                this.blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, this.blocks[i].Block, Money.Coins((long)this.federatedPegSettings.NormalDepositThresholdAmount + 1), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => this.blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(this.blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);
            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = await maturedBlocksProvider.RetrieveDepositsAsync(10);

            // Total deposits
            Assert.Equal(13, depositsResult.Value.SelectMany(b => b.Deposits).Count());

            // Small Deposits
            Assert.Empty(depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Small));

            // Normal Deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Normal).Count());

            // Large Deposits
            Assert.Equal(7, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Large).Count());
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
