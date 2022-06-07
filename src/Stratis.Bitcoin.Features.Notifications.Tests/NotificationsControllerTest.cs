using System;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Features.Notifications.Controllers;
using Stratis.Bitcoin.Features.Notifications.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Bitcoin.Tests.Common.Logging;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Xunit;

namespace Stratis.Bitcoin.Features.Notifications.Tests
{
    public class NotificationsControllerTest : LogsTestBase
    {
        private readonly Network network;

        public NotificationsControllerTest()
        {
            this.network = new StraxMain();
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [Trait("Module", "NotificationsController")]
        public void Given_SyncActionIsCalled_When_QueryParameterIsNullOrEmpty_Then_ReturnBadRequest(string from)
        {
            var mockingContext = new MockingContext(ConsensusManagerHelper.GetMockingServices(this.network)
                .AddSingleton<IBlockNotification>(ctx => ctx.GetService<Mock<BlockNotification>>().Object));

            var notificationController = mockingContext.GetService<NotificationsController>();
            IActionResult result = notificationController.SyncFrom(from);

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
        }

        [Fact]
        public void Given_SyncActionIsCalled_When_ABlockHeightIsSpecified_Then_TheChainIsSyncedFromTheHash()
        {
            // Set up
            int heightLocation = 480946;
            var header = this.network.Consensus.ConsensusFactory.CreateBlockHeader();
            uint256 hash = header.GetHash();

            var chainedHeader = new ChainedHeader(header, hash, null);
            var chain = new Mock<ChainIndexer>();
            chain.Setup(c => c.GetHeader(heightLocation)).Returns(chainedHeader);

            var mockingContext = new MockingContext(ConsensusManagerHelper.GetMockingServices(this.network, chainIndexer: ctx => chain.Object)
                .AddSingleton<IBlockNotification>(ctx => ctx.GetService<Mock<BlockNotification>>().Object));

            // Act
            var notificationController = mockingContext.GetService<NotificationsController>();
            IActionResult result = notificationController.SyncFrom(heightLocation.ToString());

            // Assert
            chain.Verify(c => c.GetHeader(heightLocation), Times.Once);
            mockingContext.GetService<Mock<BlockNotification>>().Verify(b => b.SyncFrom(hash), Times.Once);
        }

        [Fact]
        public void Given_SyncActionIsCalled_When_ABlockHashIsSpecified_Then_TheChainIsSyncedFromTheHash()
        {
            // Set up
            int heightLocation = 480946;
            var header = this.network.Consensus.ConsensusFactory.CreateBlockHeader();
            uint256 hash = header.GetHash();
            string hashLocation = hash.ToString();

            var chainedHeader = new ChainedHeader(this.network.Consensus.ConsensusFactory.CreateBlockHeader(), hash, null);
            var chain = new Mock<ChainIndexer>();
            chain.Setup(c => c.GetHeader(uint256.Parse(hashLocation))).Returns(chainedHeader);

            var mockingContext = new MockingContext(ConsensusManagerHelper.GetMockingServices(this.network, chainIndexer: ctx => chain.Object)
                .AddSingleton<IBlockNotification>(ctx => ctx.GetService<Mock<BlockNotification>>().Object));

            // Act
            var notificationController = mockingContext.GetService<NotificationsController>();
            IActionResult result = notificationController.SyncFrom(hashLocation);

            // Assert
            chain.Verify(c => c.GetHeader(heightLocation), Times.Never);
            mockingContext.GetService<Mock<BlockNotification>>().Verify(b => b.SyncFrom(hash), Times.Once);
        }

        [Fact]
        public void Given_SyncActionIsCalled_When_ANonExistingBlockHashIsSpecified_Then_ABadRequestErrorIsReturned()
        {
            // Set up
            string hashLocation = "000000000000000000c03dbe6ee5fedb25877a12e32aa95bc1d3bd480d7a93f9";

            var chain = new Mock<ChainIndexer>();
            chain.Setup(c => c.GetHeader(uint256.Parse(hashLocation))).Returns((ChainedHeader)null);

            var mockingContext = new MockingContext(ConsensusManagerHelper.GetMockingServices(this.network, chainIndexer: ctx => chain.Object)
                .AddSingleton<IBlockNotification>(ctx => ctx.GetService<Mock<BlockNotification>>().Object));

            // Act
            var notificationController = mockingContext.GetService<NotificationsController>();

            // Assert
            IActionResult result = notificationController.SyncFrom(hashLocation);

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
        }

        [Fact]
        public void Given_SyncActionIsCalled_When_AnInvalidBlockHashIsSpecified_Then_AnExceptionIsThrown()
        {
            // Set up
            string hashLocation = "notAValidHash";

            var mockingContext = new MockingContext(ConsensusManagerHelper.GetMockingServices(this.network)
                .AddSingleton<IBlockNotification>(ctx => ctx.GetService<Mock<BlockNotification>>().Object));

            // Act
            var notificationController = mockingContext.GetService<NotificationsController>();

            // Assert
            Assert.Throws<FormatException>(() => notificationController.SyncFrom(hashLocation));
        }

        [Fact]
        public void Given_SyncActionIsCalled_When_HeightNotOnChain_Then_ABadRequestErrorIsReturned()
        {
            // Set up
            var chain = new Mock<ChainIndexer>();
            chain.Setup(c => c.GetHeader(15)).Returns((ChainedHeader)null);

            var mockingContext = new MockingContext(ConsensusManagerHelper.GetMockingServices(this.network, chainIndexer: ctx => chain.Object)
                .AddSingleton<IBlockNotification>(ctx => ctx.GetService<Mock<BlockNotification>>().Object));

            // Act
            var notificationController = mockingContext.GetService<NotificationsController>();

            // Assert
            IActionResult result = notificationController.SyncFrom("15");

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
        }
    }
}
