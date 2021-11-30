using System.ComponentModel.DataAnnotations;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Base;
using Stratis.Bitcoin.Controllers.Models;
using Stratis.Bitcoin.Features.BlockStore.AddressIndexing;
using Stratis.Bitcoin.Features.BlockStore.Controllers;
using Stratis.Bitcoin.Features.BlockStore.Models;
using Stratis.Bitcoin.Interfaces;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Wallet.Common;
using Stratis.Bitcoin.Utilities.JsonErrors;
using Xunit;

namespace Stratis.Bitcoin.Features.BlockStore.Tests
{
    public class BlockStoreControllerTests
    {
        private const string BlockAsHex = "07000000a67060c12de88468739e24b95f87cd6765ff3386945bb2c77c3023e062e364187eea2ef32bf39dae67fd846e0520b980d462ec29a66340d19e9b62f82ccadf3e00000000ffff001d000000000201000000010000000000000000000000000000000000000000000000000000000000000000ffffffff00ffffffff01640000000000000000000000000100000001a5446c8e54f3bd0005ce57e12a4650a4c99ea33d864867f8806f3ba3bc9096040000000000ffffffff0200000000000000000032000000000000000000000000020203";

        [Fact]
        public void GetBlock_With_null_Hash_IsInvalid()
        {
            var requestWithNoHash = new SearchByHashRequest()
            {
                Hash = null,
                OutputJson = true
            };
            var validationContext = new ValidationContext(requestWithNoHash);
            Validator.TryValidateObject(requestWithNoHash, validationContext, null, true).Should().BeFalse();
        }

        [Fact]
        public void GetBlock_With_empty_Hash_IsInvalid()
        {
            var requestWithNoHash = new SearchByHashRequest()
            {
                Hash = "",
                OutputJson = false
            };
            var validationContext = new ValidationContext(requestWithNoHash);
            Validator.TryValidateObject(requestWithNoHash, validationContext, null, true).Should().BeFalse();
        }

        [Fact]
        public void GetBlock_With_good_Hash_IsValid()
        {
            var requestWithNoHash = new SearchByHashRequest()
            {
                Hash = "some good hash",
                OutputJson = true
            };

            var validationContext = new ValidationContext(requestWithNoHash);
            Validator.TryValidateObject(requestWithNoHash, validationContext, null, true).Should().BeTrue();
        }

        [Fact]
        public void Get_Block_When_Hash_Is_Not_Found_Should_Return_OkResult_WithMessage()
        {
            (Mock<IBlockStore> store, BlockStoreController controller) = GetControllerAndStore();

            store.Setup(c => c.GetBlock(It.IsAny<uint256>())).Returns((Block)null);

            IActionResult response = controller.GetBlock(new SearchByHashRequest() { Hash = new uint256(1).ToString(), OutputJson = true });

            response.Should().BeOfType<OkObjectResult>();
            var result = (OkObjectResult)response;
            result.StatusCode.Should().Be((int)HttpStatusCode.OK);
            result.Value.Should().Be("Block not found");
        }

        [Fact]
        public void Get_Block_When_Hash_Is_Invalid_Should_Error_With_Explanation()
        {
            (Mock<IBlockStore> store, BlockStoreController controller) = GetControllerAndStore();

            IActionResult response = controller.GetBlock(new SearchByHashRequest() { Hash = "INVALID", OutputJson = true });

            response.Should().BeOfType<ErrorResult>();
            var notFoundObjectResult = (ErrorResult)response;
            notFoundObjectResult.StatusCode.Should().Be(400);
            ((ErrorResponse)notFoundObjectResult.Value).Errors[0].Description.Should().Contain("Invalid Hex String");
        }

        [Fact]
        public void Get_Block_When_Block_Is_Found_And_Requesting_JsonOuput()
        {
            (Mock<IBlockStore> store, BlockStoreController controller) = GetControllerAndStore();

            var block = Block.Parse(BlockAsHex, new StraxMain().Consensus.ConsensusFactory);

            store.Setup(c => c.GetBlock(It.IsAny<uint256>()))
                .Returns(Block.Parse(BlockAsHex, new StraxMain().Consensus.ConsensusFactory));

            IActionResult response = controller.GetBlock(new SearchByHashRequest() { Hash = block.GetHash().ToString(), OutputJson = true });

            response.Should().BeOfType<JsonResult>();
            var result = (JsonResult)response;

            result.Value.Should().BeOfType<BlockModel>();
            ((BlockModel)result.Value).Hash.Should().Be(block.GetHash().ToString());
            ((BlockModel)result.Value).MerkleRoot.Should()
                .Be("3edfca2cf8629b9ed14063a629ec62d480b920056e84fd67ae9df32bf32eea7e");
        }

        [Fact]
        public void Get_Block_When_Block_Is_Found_And_Requesting_Verbose_JsonOuput()
        {
            (Mock<IBlockStore> store, BlockStoreController controller) = GetControllerAndStore();

            var block = Block.Parse(BlockAsHex, new StraxMain().Consensus.ConsensusFactory);

            store
                .Setup(c => c.GetBlock(It.IsAny<uint256>()))
                .Returns(block);

            IActionResult response = controller.GetBlock(new SearchByHashRequest() { Hash = block.GetHash().ToString(), OutputJson = true, ShowTransactionDetails = true });

            response.Should().BeOfType<JsonResult>();
            var result = (JsonResult)response;

            result.Value.Should().BeOfType<BlockTransactionDetailsModel>();
            ((BlockTransactionDetailsModel)result.Value).Transactions.Should().HaveCountGreaterThan(1);
        }

        [Fact]
        public void Get_Block_When_Block_Is_Found_And_Requesting_RawOuput()
        {
            (Mock<IBlockStore> store, BlockStoreController controller) = GetControllerAndStore();

            var block = Block.Parse(BlockAsHex, new StraxMain().Consensus.ConsensusFactory);

            store
                .Setup(c => c.GetBlock(It.IsAny<uint256>()))
                .Returns(block);

            IActionResult response = controller.GetBlock(new SearchByHashRequest() { Hash = block.GetHash().ToString(), OutputJson = false });

            response.Should().BeOfType<JsonResult>();
            var result = (JsonResult)response;
            ((Block)(result.Value)).ToHex(new StraxMain()).Should().Be(BlockAsHex);
        }

        [Fact]
        public void GetBlockCount_ReturnsHeightFromChainState()
        {
            var logger = new Mock<ILoggerFactory>();
            var store = new Mock<IBlockStore>();
            var chainState = new Mock<IChainState>();
            var addressIndexer = new Mock<IAddressIndexer>();
            var utxoIndexer = new Mock<IUtxoIndexer>();
            var scriptAddressReader = Mock.Of<IScriptAddressReader>();

            ChainIndexer chainIndexer = WalletTestsHelpers.GenerateChainWithHeight(3, new StraxTest());

            logger.Setup(l => l.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>);

            chainState.Setup(c => c.ConsensusTip)
                .Returns(chainIndexer.GetHeader(2));

            var controller = new BlockStoreController(new StraxMain(), logger.Object, store.Object, chainState.Object, chainIndexer, addressIndexer.Object, utxoIndexer.Object, scriptAddressReader);

            var json = (JsonResult)controller.GetBlockCount();
            int result = int.Parse(json.Value.ToString());

            Assert.Equal(2, result);
        }

        private static (Mock<IBlockStore> store, BlockStoreController controller) GetControllerAndStore()
        {
            var logger = new Mock<ILoggerFactory>();
            var store = new Mock<IBlockStore>();
            var chainState = new Mock<IChainState>();
            var addressIndexer = new Mock<IAddressIndexer>();
            var utxoIndexer = new Mock<IUtxoIndexer>();
            var scriptAddressReader = Mock.Of<IScriptAddressReader>();

            logger.Setup(l => l.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>);

            var chain = new Mock<ChainIndexer>();
            Block block = Block.Parse(BlockAsHex, new StraxMain().Consensus.ConsensusFactory);
            chain.Setup(c => c.GetHeader(It.IsAny<uint256>())).Returns(new ChainedHeader(block.Header, block.Header.GetHash(), 1));
            chain.Setup(x => x.Tip).Returns(new ChainedHeader(block.Header, block.Header.GetHash(), 1));

            var controller = new BlockStoreController(new StraxMain(), logger.Object, store.Object, chainState.Object, chain.Object, addressIndexer.Object, utxoIndexer.Object, scriptAddressReader);

            return (store, controller);
        }
    }
}
