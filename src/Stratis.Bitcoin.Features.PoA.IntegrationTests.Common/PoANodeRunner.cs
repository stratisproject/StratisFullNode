﻿using Stratis.Bitcoin.Builder;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.Api;
using Stratis.Bitcoin.Features.BlockStore;
using Stratis.Bitcoin.Features.MemoryPool;
using Stratis.Bitcoin.Features.RPC;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Features.Wallet;
using Stratis.Bitcoin.IntegrationTests.Common;
using Stratis.Bitcoin.IntegrationTests.Common.Runners;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Utilities;
using Stratis.Features.Collateral.CounterChain;
using Stratis.Features.SQLiteWalletRepository;

namespace Stratis.Bitcoin.Features.PoA.IntegrationTests.Common
{
    public class PoANodeRunner : NodeRunner
    {
        private readonly IDateTimeProvider timeProvider;

        public PoANodeRunner(string dataDir, PoANetwork network, EditableTimeProvider timeProvider)
            : base(dataDir, null)
        {
            this.Network = network;
            this.timeProvider = timeProvider;
        }

        public override void BuildNode()
        {
            var settings = new NodeSettings(this.Network, args: new string[] { "-conf=poa.conf", "-datadir=" + this.DataFolder });

            this.FullNode = (FullNode)new FullNodeBuilder()
                .UseNodeSettings(settings)
                .UseBlockStore()
                .AddPoAFeature()
                .UsePoAConsensus()
                .AddPoAMiningCapability<PoABlockDefinition>()
                .SetCounterChainNetwork(new StraxRegTest())
                .UseMempool()
                .UseWallet()
                .AddSQLiteWalletRepository()
                .UseApi()
                .AddRPC()
                .MockIBD()
                .UseTestChainedHeaderTree()
                .ReplaceTimeProvider(this.timeProvider)
                .AddFastMiningCapability()
                .Build();
        }
    }
}
