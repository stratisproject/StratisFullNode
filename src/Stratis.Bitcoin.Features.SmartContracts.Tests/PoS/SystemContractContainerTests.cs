using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.Features.SmartContracts.Tests.PoS
{
    public class TestSystemContract
    {

    }

    public class SystemContractContainerTests
    {
        [Fact]
        public void CanUseSystemContractContainer()
        {
            var network = new StraxMain();
            KeyId contractId = new Key().PubKey.Hash;
            var container = new SystemContractContainer(
                network,
                new Dictionary<KeyId, Type> { { contractId, typeof(TestSystemContract) }},
                new Dictionary<uint256, (int start, int? end)[]> { { SystemContractContainer.GetPseudoHash(contractId, 1), new[] { (1, (int?)10) } } },
                new Dictionary<uint256, (string, bool)> { });

            uint256 hash = container.GetContractHashes().First();

            Assert.True(SystemContractContainer.IsPseudoHash(hash));

            (Type contractType, uint version) typeAndVersion = container.GetContractTypeAndVersion(hash);

            Assert.Equal(typeof(TestSystemContract), typeAndVersion.contractType);
            Assert.Equal((uint)1, typeAndVersion.version);

            ChainedHeader chainedHeader = new ChainedHeader(0, null, null) { };
            var mockChainStore = new Mock<IChainStore>();
            BlockHeader header = network.Consensus.ConsensusFactory.CreateBlockHeader();
            mockChainStore.Setup(x => x.GetHeader(It.IsAny<ChainedHeader>(), It.IsAny<uint256>())).Returns(header);
            chainedHeader.SetPrivatePropertyValue("ChainStore", mockChainStore.Object);

            // Active if previous header is at height 9.
            chainedHeader.SetPrivatePropertyValue("Height", 9);
            Assert.True(container.IsActive(hash, chainedHeader, (h, d) => true));

            // Inactive if previous header is at height 10.
            chainedHeader.SetPrivatePropertyValue("Height", 10);
            Assert.False(container.IsActive(hash, chainedHeader, (h, d) => true));
        }
    }
}
