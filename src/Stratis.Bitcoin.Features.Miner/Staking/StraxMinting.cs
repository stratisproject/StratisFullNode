using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Configuration.Logging;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Interfaces;
using Stratis.Bitcoin.Features.Consensus.Rules.CommonRules;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Mining;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Miner.Staking
{
    public class StraxMinting : PosMinting
    {
        public StraxMinting(
            IBlockProvider blockProvider,
            IConsensusManager consensusManager,
            ChainIndexer chainIndexer,
            Network network,
            IDateTimeProvider dateTimeProvider,
            IInitialBlockDownloadState initialBlockDownloadState,
            INodeLifetime nodeLifetime,
            ICoinView coinView,
            IStakeChain stakeChain,
            IStakeValidator stakeValidator,
            MempoolSchedulerLock mempoolLock,
            ITxMempool mempool,
            IWalletManager walletManager,
            IAsyncProvider asyncProvider,
            ITimeSyncBehaviorState timeSyncBehaviorState,
            ILoggerFactory loggerFactory,
            MinerSettings minerSettings) : base(blockProvider, consensusManager, chainIndexer, network, dateTimeProvider,
                initialBlockDownloadState, nodeLifetime, coinView, stakeChain, stakeValidator, mempoolLock, mempool,
                walletManager, asyncProvider, timeSyncBehaviorState, loggerFactory, minerSettings)
        {
        }

        public override Transaction PrepareCoinStakeTransactions(int currentChainHeight, CoinstakeContext coinstakeContext, long coinstakeOutputValue, int utxosCount, long amountStaked, long reward)
        {
            long cirrusReward = reward * StraxCoinviewRule.CirrusRewardPercentage / 100;

            coinstakeOutputValue -= cirrusReward;

            // Populate the initial coinstake with the modified overall reward amount, the outputs will be split as necessary
            base.PrepareCoinStakeTransactions(currentChainHeight, coinstakeContext, coinstakeOutputValue, utxosCount, amountStaked, reward);

            // Now add the remaining reward into an additional output on the coinstake
            var cirrusRewardOutput = new TxOut(cirrusReward, StraxCoinstakeRule.CirrusRewardScript);
            coinstakeContext.CoinstakeTx.Outputs.Add(cirrusRewardOutput);

            return coinstakeContext.CoinstakeTx;
        }
    }
}
