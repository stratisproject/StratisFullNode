using Moq;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Bitcoin.Features.SmartContracts.Tests.PoS
{
    public class SmartContractActivationProviderTests
    {
        [Fact]
        public void WhenSmartContractsNotActiveSkipRuleReturnsTrue()
        {
            var network = new StraxRegTest();
            var chainIndexer = new ChainIndexer(network);
            var nodeDeployments = new NodeDeployments(network, chainIndexer);
            var activationProvider = new SmartContractPosActivationProvider(network, nodeDeployments, chainIndexer);
            activationProvider.IsActive = (ch) => false;
            var ruleContext = new RuleContext();
            ruleContext.ValidationContext = new ValidationContext() {  ChainedHeaderToValidate = new ChainedHeader(0, null, null) { } };
            Assert.True(activationProvider.SkipRule(ruleContext));
        }

        [Fact]
        public void WhenSmartContractsActiveAndSmartContractHeaderSkipRuleReturnsFalse()
        {
            var network = new StraxRegTest();
            var chainIndexer = new ChainIndexer(network);
            var nodeDeployments = new NodeDeployments(network, chainIndexer);
            var activationProvider = new SmartContractPosActivationProvider(network, nodeDeployments, chainIndexer);
            activationProvider.IsActive = (ch) => true;
            var ruleContext = new RuleContext();
            ChainedHeader chainedHeader = new ChainedHeader(0, null, null) { };
            var mockChainStore = new Mock<IChainStore>();
            BlockHeader header = network.Consensus.ConsensusFactory.CreateBlockHeader();
            header.Version |= PosBlockHeader.ExtendedHeaderBit;
            mockChainStore.Setup(x => x.GetHeader(It.IsAny<ChainedHeader>(), It.IsAny<uint256>())).Returns(header);
            chainedHeader.SetPrivatePropertyValue("ChainStore", mockChainStore.Object);
            
            ruleContext.ValidationContext = new ValidationContext() { ChainedHeaderToValidate = chainedHeader };
            Assert.False(activationProvider.SkipRule(ruleContext));
        }

        [Fact]
        public void WhenSmartContractsActiveAndNotSmartContractHeaderThrowsException()
        {
            var network = new StraxRegTest();
            var chainIndexer = new ChainIndexer(network);
            var nodeDeployments = new NodeDeployments(network, chainIndexer);
            var activationProvider = new SmartContractPosActivationProvider(network, nodeDeployments, chainIndexer);
            activationProvider.IsActive = (ch) => true;
            var ruleContext = new RuleContext();
            ChainedHeader chainedHeader = new ChainedHeader(0, null, null) { };
            var mockChainStore = new Mock<IChainStore>();
            mockChainStore.Setup(x => x.GetHeader(It.IsAny<ChainedHeader>(), It.IsAny<uint256>())).Returns(network.Consensus.ConsensusFactory.CreateBlockHeader());
            chainedHeader.SetPrivatePropertyValue("ChainStore", mockChainStore.Object);

            ruleContext.ValidationContext = new ValidationContext() { ChainedHeaderToValidate = chainedHeader };
            Assert.Throws<ConsensusErrorException>(() => activationProvider.SkipRule(ruleContext));
        }
    }
}
