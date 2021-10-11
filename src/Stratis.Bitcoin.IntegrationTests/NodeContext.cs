using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.IntegrationTests
{
    public class NodeContext : IDisposable
    {
        /// <summary>Factory for creating loggers.</summary>
        protected readonly ILoggerFactory loggerFactory;

        private readonly List<IDisposable> cleanList;

        public NodeContext(object caller, string name, Network network)
        {
            network ??= KnownNetworks.RegTest;
            this.loggerFactory = new LoggerFactory();
            this.Network = network;
            this.FolderName = TestBase.CreateTestDir(caller, name);
            var dateTimeProvider = new DateTimeProvider();
            var serializer = new DBreezeSerializer(this.Network.Consensus.ConsensusFactory);
            this.Coindb = new LevelDbCoindb(network, this.FolderName, dateTimeProvider, new NodeStats(dateTimeProvider, NodeSettings.Default(network), new Mock<IVersionProvider>().Object), serializer);
            this.Coindb.Initialize(new ChainedHeader(network.GetGenesis().Header, network.GenesisHash, 0));
            this.cleanList = new List<IDisposable> { (IDisposable)this.Coindb };
        }

        public Network Network { get; }

        private ChainBuilder chainBuilder;

        public ChainBuilder ChainBuilder
        {
            get
            {
                return this.chainBuilder = this.chainBuilder ?? new ChainBuilder(this.Network);
            }
        }

        public ICoindb Coindb { get; private set; }

        public string FolderName { get; }

        public static NodeContext Create(object caller, [CallerMemberName] string name = null, Network network = null, bool clean = true)
        {
            return new NodeContext(caller, name, network);
        }

        public void Dispose()
        {
            foreach (IDisposable item in this.cleanList)
                item.Dispose();
        }

        public void ReloadPersistentCoinView(ChainedHeader chainTip)
        {
            ((IDisposable)this.Coindb).Dispose();
            this.cleanList.Remove((IDisposable)this.Coindb);
            var dateTimeProvider = new DateTimeProvider();
            var serializer = new DBreezeSerializer(this.Network.Consensus.ConsensusFactory);
            this.Coindb = new LevelDbCoindb(this.Network, this.FolderName, dateTimeProvider, new NodeStats(dateTimeProvider, NodeSettings.Default(this.Network), new Mock<IVersionProvider>().Object), serializer);

            this.Coindb.Initialize(chainTip);
            this.cleanList.Add((IDisposable)this.Coindb);
        }
    }
}
