using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NSubstitute;
using NSubstitute.Core;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
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
        private readonly IConsensusManager consensusManager;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly TestTransactionBuilder transactionBuilder;
        private readonly byte[] opReturnBytes;
        private readonly BitcoinPubKeyAddress targetAddress;

        public MaturedBlocksProviderTests()
        {
            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.logger = Substitute.For<ILogger>();
            this.loggerFactory.CreateLogger(null).ReturnsForAnyArgs(this.logger);
            this.consensusManager = Substitute.For<IConsensusManager>();
            this.network = CirrusNetwork.NetworksSelector.Regtest();

            this.opReturnDataReader = Substitute.For<IOpReturnDataReader>();
            this.opReturnDataReader.TryGetTargetAddress(null, out string address).Returns(callInfo => { callInfo[1] = null; return false; });

            this.transactionBuilder = new TestTransactionBuilder();

            this.addressHelper = new MultisigAddressHelper(this.network, Networks.Strax.Regtest());
            this.targetAddress = this.addressHelper.GetNewTargetChainPubKeyAddress();
            this.opReturnBytes = Encoding.UTF8.GetBytes(this.targetAddress.ToString());

            this.federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            this.federatedPegSettings.MultiSigRedeemScript.Returns(this.addressHelper.PayToMultiSig);
            this.federatedPegSettings.MinimumConfirmationsSmallDeposits.Returns(5);
            this.federatedPegSettings.SmallDepositThresholdAmount.Returns(Money.Coins(10));
            this.federatedPegSettings.MinimumConfirmationsNormalDeposits.Returns(10);
        }

        [Fact]
        public void GetMaturedBlocksReturnsDeposits()
        {
            List<ChainedHeaderBlock> blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(10, null, true);

            ChainedHeader tip = blocks.Last().ChainedHeader;

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });

            IFederatedPegSettings federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            federatedPegSettings.MinimumConfirmationsNormalDeposits.Returns(0);

            var deposits = new List<IDeposit>() { new Deposit(new uint256(0), DepositRetrievalType.Normal, 100, "test", 0, new uint256(1)) };

            IDepositExtractor depositExtractor = Substitute.For<IDepositExtractor>();
            depositExtractor.ExtractBlockDeposits(blocks.First(), DepositRetrievalType.Normal).ReturnsForAnyArgs(new MaturedBlockDepositsModel(new MaturedBlockInfoModel(), deposits));
            this.consensusManager.Tip.Returns(tip);

            // Makes every block a matured block.
            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, federatedPegSettings, this.loggerFactory);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = maturedBlocksProvider.RetrieveDeposits(0);

            // This will be double the amount of blocks because the mocked depositExtractor will always return a set of blocks
            // as that is how it has been configured.
            Assert.Equal(22, depositsResult.Value.Count);
        }

        /// <summary>
        /// Scenario 1
        /// 
        /// Tip                     = 20
        /// Faster Deposits from    = 5 to 9
        /// Normal Deposists from   = 11 to 17
        /// Faster Min Confirms     = 5
        /// Normal Min Confirms     = 10
        /// Retrieve from           = 5
        /// 
        /// Returns 0 normal deposits
        /// Returns 4 faster deposits
        /// </summary>
        [Fact]
        public void RetrieveDeposits_ReturnsFasterAndNormalDeposits_Scenario1()
        {
            // Create a "chain" of 20 blocks.
            List<ChainedHeaderBlock> blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(20, null, true);

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i < 17; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 4 faster deposits to blocks 5 through to 9 (the amounts are less than 10).
            for (int i = 5; i < 9; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.loggerFactory, this.federatedPegSettings, this.opReturnDataReader);

            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings, this.loggerFactory);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = maturedBlocksProvider.RetrieveDeposits(5);

            // Total deposits
            Assert.Equal(4, depositsResult.Value.SelectMany(b => b.Deposits).Count());

            // Normal Deposits
            Assert.Empty(depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Normal));

            // Faster Deposits
            Assert.Equal(4, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Small).Count());
        }

        /// <summary>
        /// Scenario 2
        /// 
        /// Tip                     = 30
        /// Faster Deposits from    = 5 to 9
        /// Normal Deposists from   = 11 to 17
        /// Faster Min Confirms     = 5
        /// Normal Min Confirms     = 10
        /// Retrieve from heigt     = 5
        /// 
        /// Returns 6 normal deposits
        /// Returns 4 faster deposits
        /// </summary>
        [Fact]
        public void RetrieveDeposits_ReturnsFasterAndNormalDeposits_Scenario2()
        {
            // Create a "chain" of 30 blocks.
            List<ChainedHeaderBlock> blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, null, true);

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i < 17; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 4 faster deposits to blocks 5 through to 9 (the amounts are less than 10).
            for (int i = 5; i < 9; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.loggerFactory, this.federatedPegSettings, this.opReturnDataReader);

            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings, this.loggerFactory);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = maturedBlocksProvider.RetrieveDeposits(5);

            // Total deposits
            Assert.Equal(10, depositsResult.Value.SelectMany(b => b.Deposits).Count());

            // Normal Deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Normal).Count());

            // Faster Deposits
            Assert.Equal(4, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Small).Count());
        }

        /// <summary>
        /// Scenario 3 (Overlaps)
        /// 
        /// Tip                     = 30
        /// Faster Deposits from    = 8 to 13
        /// Normal Deposists from   = 11 to 15
        /// Faster Min Confirms     = 5
        /// Normal Min Confirms     = 10
        /// Retrieve from heigt     = 5
        /// 
        /// Returns 6 faster deposits
        /// Returns 5 normal deposits
        /// </summary>
        [Fact]
        public void RetrieveDeposits_ReturnsFasterAndNormalDeposits_Scenario3()
        {
            // Create a "chain" of 30 blocks.
            List<ChainedHeaderBlock> blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, null, true);

            // Add 6 faster deposits to blocks 8 through to 13 (the amounts are less than 10).
            for (int i = 8; i <= 13; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(8), this.opReturnBytes);
            }

            // Add 5 normal deposits to block 11 through to 15.
            for (int i = 11; i <= 15; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.loggerFactory, this.federatedPegSettings, this.opReturnDataReader);

            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings, this.loggerFactory);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = maturedBlocksProvider.RetrieveDeposits(5);

            // Total deposits
            Assert.Equal(11, depositsResult.Value.SelectMany(b => b.Deposits).Count());

            // Faster Deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Small).Count());

            // Normal Deposits
            Assert.Equal(5, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Normal).Count());
        }

        /// <summary>
        /// Scenario 4
        /// 
        /// Tip                     = 30
        /// Faster Deposits from    = 5 to 9
        /// Normal Deposists from   = 11 to 17
        /// Faster Min Confirms     = 5
        /// Normal Min Confirms     = 10
        /// Retrieve from           = 10
        /// 
        /// Returns 6 normal deposits
        /// Returns 0 faster deposits
        /// </summary>
        [Fact]
        public void RetrieveDeposits_ReturnsFasterAndNormalDeposits_Scenario4()
        {
            // Create a "chain" of 20 blocks.
            List<ChainedHeaderBlock> blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, null, true);

            // Add 4 faster deposits to blocks 5 through to 8 (the amounts are less than 10).
            for (int i = 5; i <= 8; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i <= 16; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.loggerFactory, this.federatedPegSettings, this.opReturnDataReader);

            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings, this.loggerFactory);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = maturedBlocksProvider.RetrieveDeposits(10);

            // Total deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Count());

            // Faster Deposits
            Assert.Empty(depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Small));

            // Normal Deposits
            Assert.Equal(6, depositsResult.Value.SelectMany(b => b.Deposits).Where(d => d.RetrievalType == DepositRetrievalType.Normal).Count());
        }

        /// <summary>
        /// Scenario 4
        /// 
        /// Tip                     = 20
        /// Faster Deposits from    = 5 to 8
        /// Normal Deposists from   = 11 to 16
        /// Faster Min Confirms     = 5
        /// Normal Min Confirms     = 10
        /// Retrieve from           = 10
        /// 
        /// Returns 0 normal deposits
        /// Returns 0 faster deposits
        /// </summary>
        [Fact]
        public void RetrieveDeposits_ReturnsFasterAndNormalDeposits_Scenario5()
        {
            // Create a "chain" of 20 blocks.
            List<ChainedHeaderBlock> blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(20, null, true);

            // Add 4 faster deposits to blocks 5 through to 8 (the amounts are less than 10).
            for (int i = 5; i <= 8; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            // Add 6 normal deposits to block 11 through to 16.
            for (int i = 11; i <= 16; i++)
            {
                blocks[i].Block.AddTransaction(new Transaction());
                CreateDepositTransaction(this.targetAddress, blocks[i].Block, Money.Coins(i), this.opReturnBytes);
            }

            this.consensusManager.GetBlockData(Arg.Any<List<uint256>>()).Returns(delegate (CallInfo info)
            {
                var hashes = (List<uint256>)info[0];
                return hashes.Select((hash) => blocks.Single(x => x.ChainedHeader.HashBlock == hash)).ToArray();
            });
            this.consensusManager.Tip.Returns(blocks.Last().ChainedHeader);

            var depositExtractor = new DepositExtractor(this.loggerFactory, this.federatedPegSettings, this.opReturnDataReader);

            var maturedBlocksProvider = new MaturedBlocksProvider(this.consensusManager, depositExtractor, this.federatedPegSettings, this.loggerFactory);

            SerializableResult<List<MaturedBlockDepositsModel>> depositsResult = maturedBlocksProvider.RetrieveDeposits(10);

            // Total deposits
            Assert.Empty(depositsResult.Value.SelectMany(b => b.Deposits));
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