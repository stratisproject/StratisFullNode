using System.Threading.Tasks;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.AsyncWork;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Tests.Consensus
{
    public class FinalizedBlockInfoRepositoryTest : TestBase
    {
        public FinalizedBlockInfoRepositoryTest() : base(KnownNetworks.StraxRegTest)
        {
        }

        [Fact]
        public async Task FinalizedHeightSavedOnDiskAsync()
        {
            string dir = CreateTestDir(this);
            var kvRepo = new KeyValueRepository(dir, new DBreezeSerializer(this.Network.Consensus.ConsensusFactory));
            var asyncMock = new Mock<IAsyncProvider>();
            asyncMock.Setup(a => a.RegisterTask(It.IsAny<string>(), It.IsAny<Task>()));

            using (var repo = new FinalizedBlockInfoRepository(kvRepo, asyncMock.Object))
            {
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
            var kvRepo = new KeyValueRepository(dir, new DBreezeSerializer(this.Network.Consensus.ConsensusFactory));
            var asyncMock = new Mock<IAsyncProvider>();
            asyncMock.Setup(a => a.RegisterTask(It.IsAny<string>(), It.IsAny<Task>()));

            using (var repo = new FinalizedBlockInfoRepository(kvRepo, asyncMock.Object))
            {
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
