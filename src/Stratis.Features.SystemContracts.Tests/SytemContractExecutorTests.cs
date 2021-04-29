using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Moq;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.Features.SystemContracts.Compatibility;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.RuntimeObserver;
using Xunit;

namespace Stratis.Features.SystemContracts.Tests
{
    public class SytemContractExecutorTests
    {
        [Fact]
        public void Should_Check_Whitelist_And_Fail()
        {
            var logsMock = new Mock<ILoggerFactory>();
            logsMock.Setup(c => c.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());

            var root = new byte[] { };
            var stateMock = new Mock<IStateRepositoryRoot>();
            stateMock.Setup(s => s.Root).Returns(root);

            var callData = new ContractTxData(1, 0, (Gas)0, null);
            var callDataSerializerMock = new Mock<ICallDataSerializer>();
            callDataSerializerMock.Setup(s => s.Deserialize(It.IsAny<byte[]>())).Returns(Result.Ok(callData));

            var whitelist = new Mock<IWhitelistedHashChecker>();
            whitelist.Setup(w => w.CheckHashWhitelisted(It.IsAny<byte[]>())).Returns(false);

            var executor = new SystemContractExecutor(
                logsMock.Object, 
                Mock.Of<ISystemContractRunner>(), 
                callDataSerializerMock.Object, 
                whitelist.Object,
                stateMock.Object);

            executor.Execute(Mock.Of<IContractTransactionContext>());

            whitelist.Verify(x => x.CheckHashWhitelisted(It.IsAny<byte[]>()), Times.Once);
            stateMock.Verify(x => x.SyncToRoot(root), Times.Never);
        }

        [Fact]
        public void Should_Check_Whitelist_And_Succeed_State_Doesnt_Change()
        {
            var logsMock = new Mock<ILoggerFactory>();
            logsMock.Setup(c => c.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());

            var root = new byte[] { };
            var stateMock = new Mock<IStateRepositoryRoot>();
            stateMock.Setup(s => s.Root).Returns(root);

            var callData = new ContractTxData(1, 0, (Gas)0, null);
            var callDataSerializerMock = new Mock<ICallDataSerializer>();
            callDataSerializerMock.Setup(s => s.Deserialize(It.IsAny<byte[]>())).Returns(Result.Ok(callData));

            var whitelist = new Mock<IWhitelistedHashChecker>();
            whitelist.Setup(w => w.CheckHashWhitelisted(It.IsAny<byte[]>())).Returns(true);

            var newState = new Mock<IStateRepositoryRoot>();
            newState.Setup(s => s.Root).Returns(root);

            var runner = new Mock<ISystemContractRunner>();
            runner.Setup(r => r.Execute(It.IsAny<ISystemContractTransactionContext>())).Returns(new SystemContractRunnerResult(newState.Object));

            var executor = new SystemContractExecutor(
                logsMock.Object,
                runner.Object,
                callDataSerializerMock.Object,
                whitelist.Object,
                stateMock.Object);

            executor.Execute(Mock.Of<IContractTransactionContext>());

            whitelist.Verify(x => x.CheckHashWhitelisted(It.IsAny<byte[]>()), Times.Once);
            stateMock.Verify(x => x.SyncToRoot(root), Times.Never);
        }

        [Fact]
        public void Should_Check_Whitelist_And_Succeed_State_Changes()
        {
            var logsMock = new Mock<ILoggerFactory>();
            logsMock.Setup(c => c.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());

            var root = new byte[] { };
            var stateMock = new Mock<IStateRepositoryRoot>();
            stateMock.Setup(s => s.Root).Returns(root);

            var callData = new ContractTxData(1, 0, (Gas)0, null);
            var callDataSerializerMock = new Mock<ICallDataSerializer>();
            callDataSerializerMock.Setup(s => s.Deserialize(It.IsAny<byte[]>())).Returns(Result.Ok(callData));

            var whitelist = new Mock<IWhitelistedHashChecker>();
            whitelist.Setup(w => w.CheckHashWhitelisted(It.IsAny<byte[]>())).Returns(true);

            var newRoot = new byte[] { 1 };
            var newState = new Mock<IStateRepositoryRoot>();
            newState.Setup(s => s.Root).Returns(newRoot);

            var runner = new Mock<ISystemContractRunner>();
            runner.Setup(r => r.Execute(It.IsAny<ISystemContractTransactionContext>())).Returns(new SystemContractRunnerResult(newState.Object));

            var executor = new SystemContractExecutor(
                logsMock.Object,
                runner.Object,
                callDataSerializerMock.Object,
                whitelist.Object,
                stateMock.Object);

            executor.Execute(Mock.Of<IContractTransactionContext>());

            whitelist.Verify(x => x.CheckHashWhitelisted(It.IsAny<byte[]>()), Times.Once);
            stateMock.Verify(x => x.SyncToRoot(newRoot), Times.Once);
        }
    }
}
