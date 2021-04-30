using System;
using System.Collections.Generic;
using System.Linq;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Stratis.SmartContracts.CLR;
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
            EmbeddedContractIdentifier contractId = new EmbeddedContractIdentifier(1, 1);
            var container = new SystemContractContainer(
                network,
                new Dictionary<ulong, string> { { contractId.ContractTypeId, typeof(TestSystemContract).ToString() }},
                new Dictionary<uint160, (int start, int? end)[]> { { contractId, new[] { (1, (int?)10) } } },
                new Dictionary<uint160, (string, bool)> { { contractId, ("SystemContracts", true) } },
                null);

            uint160 id = container.GetContractIdentifiers().First();

            Assert.True(EmbeddedContractIdentifier.IsEmbedded(contractId));

            (string contractType, uint version) typeAndVersion = container.GetContractTypeAndVersion(id);

            Assert.Equal(typeof(TestSystemContract).ToString(), typeAndVersion.contractType);
            Assert.Equal((uint)1, typeAndVersion.version);

            ChainedHeader chainedHeader = new ChainedHeader(0, null, null) { };
            var mockChainStore = new Mock<IChainStore>();
            BlockHeader header = network.Consensus.ConsensusFactory.CreateBlockHeader();
            mockChainStore.Setup(x => x.GetHeader(It.IsAny<ChainedHeader>(), It.IsAny<uint256>())).Returns(header);
            chainedHeader.SetPrivatePropertyValue("ChainStore", mockChainStore.Object);

            // Active if previous header is at height 9.
            chainedHeader.SetPrivatePropertyValue("Height", 9);
            Assert.True(container.IsActive(id, chainedHeader, (h, d) => false));

            // Inactive if previous header is at height 10.
            chainedHeader.SetPrivatePropertyValue("Height", 10);
            Assert.False(container.IsActive(id, chainedHeader, (h, d) => false));

            // Inactive if previous header is at height 10 unless activated by BIP 9.
            chainedHeader.SetPrivatePropertyValue("Height", 10);
            Assert.True(container.IsActive(id, chainedHeader, (h, d) => true));
        }

        [Fact]
        public void CanDereferenceContractTypes()
        {
            foreach (Network network in new Network[] { new StraxMain(), new StraxTest(), new StraxRegTest() })
            {
                foreach (uint160 id in network.SystemContractContainer.GetContractIdentifiers())
                {
                    (string typeName, uint version) = network.SystemContractContainer.GetContractTypeAndVersion(id);

                    Assert.NotNull(Type.GetType(typeName));
                }
            }
        }
    }
}
