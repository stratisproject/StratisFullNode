using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NSubstitute;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Primitives;
using Stratis.Bitcoin.Signals;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.FederatedPeg.Distribution;
using Stratis.Features.FederatedPeg.Interfaces;
using Stratis.Features.FederatedPeg.SourceChain;
using Stratis.Features.FederatedPeg.Tests.Utils;
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
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILoggerFactory loggerFactory;
        private readonly StraxRegTest network;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly Signals signals;

        public RewardClaimerTests()
        {
            this.network = new StraxRegTest();
            this.addressHelper = new MultisigAddressHelper(this.network, new CirrusRegTest());
            this.broadCasterManager = Substitute.For<IBroadcasterManager>();
            this.chainIndexer = new ChainIndexer(this.network);
            this.consensusManager = Substitute.For<IConsensusManager>();
            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.signals = new Signals(this.loggerFactory, null);

            this.opReturnDataReader = new OpReturnDataReader(this.loggerFactory, new CounterChainNetworkWrapper(new CirrusRegTest()));

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
        public void RewardClaimer_RetrieveDeposits_Scenario1()
        {
            // Create a "chain" of 30 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, true, network: this.network, chainIndexer: this.chainIndexer, withCoinbaseAndCoinStake: true, createCirrusReward: true);
            var rewardClaimer = new RewardClaimer(this.broadCasterManager, this.chainIndexer, this.consensusManager, this.loggerFactory, this.network, this.signals);

            var depositExtractor = new DepositExtractor(this.federatedPegSettings, this.network, this.opReturnDataReader);

            // Add 5 distribution deposits from block 11 through to 15.
            for (int i = 11; i <= 15; i++)
            {
                Transaction rewardTransaction = rewardClaimer.BuildRewardTransaction();
                IDeposit deposit = depositExtractor.ExtractDepositFromTransaction(rewardTransaction, i, this.blocks[i].Block.GetHash());
                Assert.NotNull(deposit);
            }
        }
    }
}
