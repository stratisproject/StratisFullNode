﻿using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Persistence.KeyValueStores;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Tests.Consensus
{
    public class FinalizedBlockInfoRepositoryTest : TestBase
    {
        private readonly ILoggerFactory loggerFactory;

        public FinalizedBlockInfoRepositoryTest() : base(KnownNetworks.StraxRegTest)
        {
            this.loggerFactory = new LoggerFactory();
        }

        [Fact]
        public async Task FinalizedHeightSavedOnDiskAsync()
        {
            string dir = CreateTestDir(this);
            var kvRepo = new LevelDbKeyValueRepository(dir, new DBreezeSerializer(this.Network.Consensus.ConsensusFactory));
            var asyncMock = new Mock<IAsyncProvider>();
            asyncMock.Setup(a => a.RegisterTask(It.IsAny<string>(), It.IsAny<Task>()));

            using (var repo = new FinalizedBlockInfoRepository(kvRepo, asyncMock.Object))
            {
                repo.Initialize(new ChainedHeader(this.Network.GetGenesis().Header, this.Network.GetGenesis().GetHash(), 0));
                repo.SaveFinalizedBlockHashAndHeight(uint256.One, 777);
            }

            using (var repo = new FinalizedBlockInfoRepository(kvRepo, asyncMock.Object))
            {
                await repo.LoadFinalizedBlockInfoAsync(this.Network);
                Assert.Equal(777, repo.GetFinalizedBlockInfo().Height);
            }
        }

        [Fact]
        public async Task FinalizedHeightCantBeDecreasedAsync()
        {
            string dir = CreateTestDir(this);
            var kvRepo = new LevelDbKeyValueRepository(dir, new DBreezeSerializer(this.Network.Consensus.ConsensusFactory));
            var asyncMock = new Mock<IAsyncProvider>();
            asyncMock.Setup(a => a.RegisterTask(It.IsAny<string>(), It.IsAny<Task>()));

            using (var repo = new FinalizedBlockInfoRepository(kvRepo, asyncMock.Object))
            {
                repo.Initialize(new ChainedHeader(this.Network.GetGenesis().Header, this.Network.GetGenesis().GetHash(), 0));
                repo.SaveFinalizedBlockHashAndHeight(uint256.One, 777);
                repo.SaveFinalizedBlockHashAndHeight(uint256.One, 555);

                Assert.Equal(777, repo.GetFinalizedBlockInfo().Height);
            }

            using (var repo = new FinalizedBlockInfoRepository(kvRepo, asyncMock.Object))
            {
                await repo.LoadFinalizedBlockInfoAsync(this.Network);
                Assert.Equal(777, repo.GetFinalizedBlockInfo().Height);
            }
        }
    }
}
