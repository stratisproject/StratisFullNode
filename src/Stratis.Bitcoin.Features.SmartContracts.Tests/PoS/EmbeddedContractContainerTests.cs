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
    public class EmbeddedContractContainerTests
    {
        [Fact]
        public void CanUseEmbeddedContractContainer()
        {
            var network = new StraxMain();
            EmbeddedContractAddress contractId = new EmbeddedContractAddress(typeof(Authentication), 1);
            var container = new EmbeddedContractContainer(
                network,
                new List<EmbeddedContractVersion> {
                    { new EmbeddedContractVersion(typeof(Authentication), 1, new[] { (1, (int?)10) }, "SystemContracts", true) } },
                null);

            uint160 address = container.GetEmbeddedContractAddresses().First();

            Assert.True(EmbeddedContractAddress.IsEmbedded(contractId));

            Assert.True(container.TryGetContractTypeAndVersion(address, out string contractType, out uint version));

            Assert.Equal(typeof(Authentication).AssemblyQualifiedName, contractType);
            Assert.Equal(contractId.Version, version);

            ChainedHeader chainedHeader = new ChainedHeader(0, null, null) { };
            var mockChainStore = new Mock<IChainStore>();
            BlockHeader header = network.Consensus.ConsensusFactory.CreateBlockHeader();
            mockChainStore.Setup(x => x.GetHeader(It.IsAny<ChainedHeader>(), It.IsAny<uint256>())).Returns(header);
            chainedHeader.SetPrivatePropertyValue("ChainStore", mockChainStore.Object);

            // Active if previous header is at height 9.
            chainedHeader.SetPrivatePropertyValue("Height", 9);
            Assert.True(container.IsActive(address, chainedHeader, (h, d) => false));

            // Inactive if previous header is at height 10.
            chainedHeader.SetPrivatePropertyValue("Height", 10);
            Assert.False(container.IsActive(address, chainedHeader, (h, d) => false));

            // Inactive if previous header is at height 10 unless activated by BIP 9.
            chainedHeader.SetPrivatePropertyValue("Height", 10);
            Assert.True(container.IsActive(address, chainedHeader, (h, d) => true));
        }

        [Fact]
        public void CanDereferenceContractTypes()
        {
            foreach (Network network in new Network[] { new StraxMain(), new StraxTest(), new StraxRegTest() })
            {
                foreach (uint160 address in network.EmbeddedContractContainer.GetEmbeddedContractAddresses())
                {
                    Assert.True(network.EmbeddedContractContainer.TryGetContractTypeAndVersion(address, out string typeName, out uint version));

                    Assert.NotNull(Type.GetType(typeName));
                }
            }
        }
    }
}
