using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Tests.Common.Logging;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Features.Consensus.Tests.CoinViews
{
    public class RewindDataIndexCacheTest : LogsTestBase
    {
        public RewindDataIndexCacheTest() : base(new StraxTest())
        {
            // override max reorg to 10
            Type consensusType = typeof(NBitcoin.Consensus);
            consensusType.GetProperty("MaxReorgLength").SetValue(this.Network.Consensus, (uint)10);
        }

        [Fact]
        public void RewindDataIndex_InitialiseCache_BelowMaxReorg()
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var coinViewMock = new Mock<ICoinView>();
            this.SetupMockCoinView(coinViewMock);

            var finalizedBlockInfoRepositoryMock = new Mock<IFinalizedBlockInfoRepository>();
            finalizedBlockInfoRepositoryMock.Setup(s => s.GetFinalizedBlockInfo()).Returns(new HashHeightPair());

            var rewindDataIndexCache = new RewindDataIndexCache(dateTimeProviderMock.Object, this.Network, finalizedBlockInfoRepositoryMock.Object, new Checkpoints());

            rewindDataIndexCache.Initialize(5, coinViewMock.Object);

            var items = rewindDataIndexCache.GetMemberValue("items") as ConcurrentDictionary<OutPoint, int>;

            items.Should().HaveCount(10);
            this.CheckCache(items, 5, 1);
        }

        [Fact]
        public void RewindDataIndex_InitialiseCache()
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var coinViewMock = new Mock<ICoinView>();
            this.SetupMockCoinView(coinViewMock);

            var finalizedBlockInfoRepositoryMock = new Mock<IFinalizedBlockInfoRepository>();
            finalizedBlockInfoRepositoryMock.Setup(s => s.GetFinalizedBlockInfo()).Returns(new HashHeightPair());

            var rewindDataIndexCache = new RewindDataIndexCache(dateTimeProviderMock.Object, this.Network, finalizedBlockInfoRepositoryMock.Object, new Checkpoints());

            rewindDataIndexCache.Initialize(20, coinViewMock.Object);

            var items = rewindDataIndexCache.GetMemberValue("items") as ConcurrentDictionary<OutPoint, int>;

            items.Should().HaveCount(22);
            this.CheckCache(items, 20, 10);
        }

        [Fact]
        public void RewindDataIndex_Save()
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var coinViewMock = new Mock<ICoinView>();
            this.SetupMockCoinView(coinViewMock);

            var finalizedBlockInfoRepositoryMock = new Mock<IFinalizedBlockInfoRepository>();
            finalizedBlockInfoRepositoryMock.Setup(s => s.GetFinalizedBlockInfo()).Returns(new HashHeightPair());

            var rewindDataIndexCache = new RewindDataIndexCache(dateTimeProviderMock.Object, this.Network, finalizedBlockInfoRepositoryMock.Object, new Checkpoints());

            rewindDataIndexCache.Initialize(20, coinViewMock.Object);

            rewindDataIndexCache.SaveAndEvict(21, new Dictionary<OutPoint, int>() { { new OutPoint(new uint256(21), 0), 21 } });
            var items = rewindDataIndexCache.GetMemberValue("items") as ConcurrentDictionary<OutPoint, int>;

            items.Should().HaveCount(21);
            this.CheckCache(items, 21, 1);
        }

        [Fact]
        public void RewindDataIndex_Flush()
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var coinViewMock = new Mock<ICoinView>();
            this.SetupMockCoinView(coinViewMock);

            var finalizedBlockInfoRepositoryMock = new Mock<IFinalizedBlockInfoRepository>();
            finalizedBlockInfoRepositoryMock.Setup(s => s.GetFinalizedBlockInfo()).Returns(new HashHeightPair());

            var rewindDataIndexCache = new RewindDataIndexCache(dateTimeProviderMock.Object, this.Network, finalizedBlockInfoRepositoryMock.Object, new Checkpoints());

            rewindDataIndexCache.Initialize(20, coinViewMock.Object);

            rewindDataIndexCache.SaveAndEvict(15, null);
            var items = rewindDataIndexCache.GetMemberValue("items") as ConcurrentDictionary<OutPoint, int>;

            items.Should().HaveCount(12);
            this.CheckCache(items, 15, 9);
        }

        [Fact]
        public void RewindDataIndex_Remove()
        {
            var dateTimeProviderMock = new Mock<IDateTimeProvider>();
            var coinViewMock = new Mock<ICoinView>();
            this.SetupMockCoinView(coinViewMock);

            var finalizedBlockInfoRepositoryMock = new Mock<IFinalizedBlockInfoRepository>();
            finalizedBlockInfoRepositoryMock.Setup(s => s.GetFinalizedBlockInfo()).Returns(new HashHeightPair());

            var rewindDataIndexCache = new RewindDataIndexCache(dateTimeProviderMock.Object, this.Network, finalizedBlockInfoRepositoryMock.Object, new Checkpoints());

            rewindDataIndexCache.Initialize(20, coinViewMock.Object);

            rewindDataIndexCache.Remove(19, coinViewMock.Object);
            var items = rewindDataIndexCache.GetMemberValue("items") as ConcurrentDictionary<OutPoint, int>;

            items.Should().HaveCount(22);
            this.CheckCache(items, 19, 9);
        }


        private void CheckCache(ConcurrentDictionary<OutPoint, int> items, int tip, int bottom)
        {
            foreach (KeyValuePair<OutPoint, int> keyValuePair in items)
            {
                Assert.True(keyValuePair.Value <= tip && keyValuePair.Value >= bottom);
            }
        }

        private void SetupMockCoinView(Mock<ICoinView> coinViewMock)
        {
            // set up coinview with 2 blocks and 2 utxo per block.
            ulong index = 1;
            coinViewMock.Setup(c => c.GetRewindData(It.IsAny<int>())).Returns(() => new RewindData()
            {
                OutputsToRestore = new List<RewindDataOutput>()
                {
                    new RewindDataOutput(new OutPoint(new uint256(index), 0), new Coins(0, new TxOut(), false)),
                    new RewindDataOutput(new OutPoint(new uint256(index++), 1), new Coins(0, new TxOut(), false)),
                }
            });
        }
    }
}
