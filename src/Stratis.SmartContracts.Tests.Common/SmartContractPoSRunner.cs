using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Interfaces;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.MemoryPool.Interfaces;
using Stratis.Bitcoin.Features.Miner;
using Stratis.Bitcoin.Features.Miner.Interfaces;
using Stratis.Bitcoin.Features.Miner.Staking;
using Stratis.Bitcoin.Features.PoA.IntegrationTests.Common;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Features.SmartContracts.Wallet;
using Stratis.Bitcoin.Features.Wallet.Interfaces;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.Runners;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Mining;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.SQLiteWalletRepository;

namespace Stratis.SmartContracts.Tests.Common
{
    public class TestStraxMinting : StraxMinting
    {
        private readonly EditableTimeProvider timeProvider;
        private readonly ChainIndexer chainIndexer;
        private readonly Network network;

        public TestStraxMinting(IBlockProvider blockProvider,
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
            this.timeProvider = dateTimeProvider as EditableTimeProvider;
            this.chainIndexer = chainIndexer;
            this.network = network;
            this.stakeCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(new[] { nodeLifetime.ApplicationStopping });
        }

        public void MineBlocks(int count)
        {
            int tipHeight = this.chainIndexer.Tip.Height;
            CancellationToken token = new CancellationToken();
            if (tipHeight >= this.network.Consensus.LastPOWBlock)
            {
                tipHeight += count;

                while (this.chainIndexer.Tip.Height < tipHeight)
                {
                    for (; ; )
                    {
                        uint coinstakeTimestamp = (uint)this.timeProvider.GetAdjustedTimeAsUnixTimestamp() & ~PosConsensusOptions.StakeTimestampMask;
                        if (coinstakeTimestamp > this.lastCoinStakeSearchTime)
                            break;

                        this.timeProvider.AdjustedTimeOffset += TimeSpan.FromSeconds(1);
                    }

                    this.GenerateBlocksAsync(new List<WalletSecret>() {
                        new WalletSecret()
                        {
                            WalletPassword = "password",
                            WalletName = "mywallet"
                        }
                    }, token).GetAwaiter().GetResult();
                }
            }
        }
    }

    public sealed class SmartContractPoSRunner : NodeRunner
    {
        private readonly EditableTimeProvider timeProvider;

        public SmartContractPoSRunner(string dataDir, Network network, EditableTimeProvider timeProvider)
            : base(dataDir, null)
        {
            this.Network = network;
            this.timeProvider = timeProvider;
        }

        public override void BuildNode()
        {
            var settings = new NodeSettings(this.Network, args: new string[] { "-conf=scpos.conf", "-datadir=" + this.DataFolder, "-displayextendednodestats=true" });

            IFullNodeBuilder builder = new FullNodeBuilder()
                .UseNodeSettings(settings)
                .UseBlockStore()
                .UseMempool()
                .AddRPC()
                .AddSmartContracts(options =>
                {
                    options.UseReflectionExecutor();
                })
                .UsePosConsensus()
                .UseSmartContractWallet()
                .AddSQLiteWalletRepository()
                .UseSmartContractPosPowMining()
                .MockIBD()
                .ReplaceTimeProvider(this.timeProvider)                
                .ReplaceService<IPosMinting, TestStraxMinting>()
                .UseTestChainedHeaderTree();

            this.FullNode = (FullNode)builder.Build();
        }
    }
}