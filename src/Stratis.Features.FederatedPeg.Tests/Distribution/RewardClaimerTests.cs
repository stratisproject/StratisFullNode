using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NSubstitute;
using Stratis.Bitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.ExternalApi;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Persistence.KeyValueStores;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.FederatedPeg.Distribution;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Features.FederatedPeg.Tests.Utils;
using Stratis.Features.PoA.Collateral.CounterChain;
using Stratis.Sidechains.Networks;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests.Distribution
{
    public sealed class RewardClaimerTests
    {
        private readonly MultisigAddressHelper addressHelper;
        private List<ChainedHeaderBlock> blocks;
        private readonly IBroadcasterManager broadCasterManager;
        private readonly ChainIndexer chainIndexer;
        private readonly IConsensusManager consensusManager;
        private readonly DBreezeSerializer dbreezeSerializer;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILoggerFactory loggerFactory;
        private readonly StraxRegTest network;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly Signals signals;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;

        public RewardClaimerTests()
        {
            this.network = new StraxRegTest
            {
                RewardClaimerBatchActivationHeight = 40,
                RewardClaimerBlockInterval = 10
            };

            this.addressHelper = new MultisigAddressHelper(this.network, new CirrusRegTest());
            this.broadCasterManager = Substitute.For<IBroadcasterManager>();
            this.chainIndexer = new ChainIndexer(this.network);
            this.consensusManager = Substitute.For<IConsensusManager>();
            this.dbreezeSerializer = new DBreezeSerializer(this.network.Consensus.ConsensusFactory);

            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.signals = new Signals(this.loggerFactory, null);

            this.initialBlockDownloadState = Substitute.For<IInitialBlockDownloadState>();
            this.initialBlockDownloadState.IsInitialBlockDownload().Returns(false);

            this.opReturnDataReader = new OpReturnDataReader(new CirrusRegTest());

            this.federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            this.federatedPegSettings.MultiSigRedeemScript.Returns(this.addressHelper.PayToMultiSig);

            this.federatedPegSettings.MinimumConfirmationsSmallDeposits.Returns(5);
            this.federatedPegSettings.MinimumConfirmationsNormalDeposits.Returns(10);
            this.federatedPegSettings.MinimumConfirmationsLargeDeposits.Returns(20);

            this.federatedPegSettings.SmallDepositThresholdAmount.Returns(Money.Coins(10));
            this.federatedPegSettings.NormalDepositThresholdAmount.Returns(Money.Coins(100));
        }

        /// <summary>
        /// Scenario 1
        /// 
        /// Tip                         = 30
        /// Distribution Deposits from  = 11 to 15
        /// </summary>
        [Fact]
        public void RewardClaimer_RetrieveSingleDeposits()
        {
            DataFolder dataFolder = TestBase.CreateDataFolder(this);
            var keyValueRepository = new LevelDbKeyValueRepository(dataFolder, this.dbreezeSerializer);

            // Create a "chain" of 30 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, true, network: this.network, chainIndexer: this.chainIndexer, withCoinbaseAndCoinStake: true, createCirrusReward: true);
            using (var rewardClaimer = new RewardClaimer(this.broadCasterManager, this.chainIndexer, this.consensusManager, this.initialBlockDownloadState, keyValueRepository, this.network, this.signals))
            {
                var depositExtractor = new DepositExtractor(this.federatedPegSettings, this.network, this.opReturnDataReader, Substitute.For<ICounterChainSettings>(), Substitute.For<IHttpClientFactory>());

                // Add 5 distribution deposits from block 11 through to 15.
                for (int i = 11; i <= 15; i++)
                {
                    Transaction rewardTransaction = rewardClaimer.BuildRewardTransaction(false);
                    IDeposit deposit = depositExtractor.ExtractDepositFromTransaction(rewardTransaction, i, this.blocks[i].Block.GetHash());
                    Assert.NotNull(deposit);
                }
            }
        }

        /// <summary>
        /// Scenario 1
        /// 
        /// Tip                         = 30
        /// Distribution Deposits from  = 11 to 15
        /// </summary>
        [Fact]
        public void RewardClaimer_RetrieveBatchedDeposits()
        {
            DataFolder dataFolder = TestBase.CreateDataFolder(this);
            var keyValueRepository = new LevelDbKeyValueRepository(dataFolder, this.dbreezeSerializer);

            // Create a "chain" of 30 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, true, network: this.network, chainIndexer: this.chainIndexer, withCoinbaseAndCoinStake: true, createCirrusReward: true);

            // The reward claimer should look at block 10 to 20.
            using (var rewardClaimer = new RewardClaimer(this.broadCasterManager, this.chainIndexer, this.consensusManager, this.initialBlockDownloadState, keyValueRepository, this.network, this.signals))
            {
                Transaction rewardTransaction = rewardClaimer.BuildRewardTransaction(true);

                Assert.Equal(10, rewardTransaction.Inputs.Count);
                Assert.Equal(2, rewardTransaction.Outputs.Count);
                Assert.Equal(Money.Coins(90), rewardTransaction.TotalOut);

                var depositExtractor = new DepositExtractor(this.federatedPegSettings, this.network, this.opReturnDataReader, Substitute.For<ICounterChainSettings>(), Substitute.For<IHttpClientFactory>());
                IDeposit deposit = depositExtractor.ExtractDepositFromTransaction(rewardTransaction, 30, this.blocks[30].Block.GetHash());
                Assert.Equal(Money.Coins(90), deposit.Amount);
            }
        }
    }
}
