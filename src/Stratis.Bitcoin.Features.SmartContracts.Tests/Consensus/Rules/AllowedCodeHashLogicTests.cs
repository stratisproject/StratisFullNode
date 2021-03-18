using System.Collections.Generic;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.Bitcoin.Features.SmartContracts.PoA;
using Stratis.Bitcoin.Features.SmartContracts.PoA.Rules;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.Bitcoin.Features.SmartContracts.PoS.Rules;
using Stratis.Bitcoin.Tests.Common;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core.Hashing;
using Stratis.SmartContracts.RuntimeObserver;
using Xunit;

namespace Stratis.Bitcoin.Features.SmartContracts.Tests.Consensus.Rules
{
    public class AllowedCodeHashLogicTests
    {
        private readonly Mock<IWhitelistedHashChecker> hashChecker;
        private readonly Mock<IContractCodeHashingStrategy> hashingStrategy;

        public AllowedCodeHashLogicTests()
        {
            this.hashChecker = new Mock<IWhitelistedHashChecker>();
            this.hashingStrategy = new Mock<IContractCodeHashingStrategy>();
        }

        [Fact]
        public void System_Contracts_Should_Allow_Code_With_Signed_Hash()
        {
            var key = new Key();

            var network = new Mock<Network>();
            var consensus = new Mock<IConsensus>();
            var consensusFactory = new SmartContractPoSConsensusFactory(new List<SystemContractsSignatureRequirement>()
            {
                new SystemContractsSignatureRequirement(key.PubKey)
            });

            consensus.Setup(c => c.ConsensusFactory).Returns(consensusFactory);
            network.Object.SetPrivatePropertyValue("Consensus", consensus.Object);

            var code = RandomUtils.GetBytes(2048);

            byte[] hash = HashHelper.Keccak256(code);

            this.hashingStrategy.Setup(h => h.Hash(code)).Returns(hash);

            string[] signatures = new[] { key.SignMessage(hash) };

            var tx = new ContractTxData(1, 1000, (Gas)10000, code, signatures: signatures);

            var sut = new PoSAllowedCodeHashLogic(network.Object, this.hashingStrategy.Object);

            sut.CheckContractTransaction(tx, 0);
        }

        [Fact]
        public void System_Contracts_Should_Disallow_Code_With_Incorrectly_Signed_Hash()
        {
            var key = new Key();

            var network = new Mock<Network>();
            var consensus = new Mock<IConsensus>();
            var consensusFactory = new SmartContractPoSConsensusFactory(new List<SystemContractsSignatureRequirement>()
            {
                new SystemContractsSignatureRequirement(key.PubKey)
            });

            consensus.Setup(c => c.ConsensusFactory).Returns(consensusFactory);
            network.Object.SetPrivatePropertyValue("Consensus", consensus.Object);

            var code = RandomUtils.GetBytes(2048);

            byte[] hash = HashHelper.Keccak256(code);

            this.hashingStrategy.Setup(h => h.Hash(code)).Returns(hash);

            string[] signatures = new[] { (new Key()).SignMessage(hash) };

            var tx = new ContractTxData(1, 1000, (Gas)10000, code, signatures: signatures);

            var sut = new PoSAllowedCodeHashLogic(network.Object, this.hashingStrategy.Object);

            Assert.Throws<ConsensusErrorException>(() => sut.CheckContractTransaction(tx, 0));
        }

        [Fact]
        public void Should_Allow_Code_With_Valid_Hash()
        {
            var code = RandomUtils.GetBytes(2048);

            byte[] hash = HashHelper.Keccak256(code);

            this.hashingStrategy.Setup(h => h.Hash(code)).Returns(hash);
            this.hashChecker.Setup(h => h.CheckHashWhitelisted(hash)).Returns(true);

            var tx = new ContractTxData(1, 1000, (Gas) 10000, code);

            var sut = new AllowedCodeHashLogic(this.hashChecker.Object, this.hashingStrategy.Object);

            sut.CheckContractTransaction(tx, 0);

            this.hashChecker.Verify(h => h.CheckHashWhitelisted(hash), Times.Once);
        }

        [Fact]
        public void Should_Throw_ConsensusErrorException_If_Hash_Not_Allowed()
        {
            var code = RandomUtils.GetBytes(2048);

            byte[] hash = HashHelper.Keccak256(code);

            this.hashingStrategy.Setup(h => h.Hash(code)).Returns(hash);
            this.hashChecker.Setup(h => h.CheckHashWhitelisted(hash)).Returns(false);

            var sut = new AllowedCodeHashLogic(this.hashChecker.Object, this.hashingStrategy.Object);

            var tx = new ContractTxData(1, 1000, (Gas)10000, code);

            Assert.Throws<ConsensusErrorException>(() => sut.CheckContractTransaction(tx, 0));

            this.hashChecker.Verify(h => h.CheckHashWhitelisted(hash), Times.Once);
        }

        [Fact]
        public void Should_Not_Validate_ContractCall()
        {
            var callTx = new ContractTxData(1, 1000, (Gas)10000, uint160.Zero, "Test");

            var sut = new AllowedCodeHashLogic(this.hashChecker.Object, this.hashingStrategy.Object);

            sut.CheckContractTransaction(callTx, 0);

            this.hashingStrategy.Verify(h => h.Hash(It.IsAny<byte[]>()), Times.Never);
            this.hashChecker.Verify(h => h.CheckHashWhitelisted(It.IsAny<byte[]>()), Times.Never);
        }
    }
}
