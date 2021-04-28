using CSharpFunctionalExtensions;
using Moq;
using NBitcoin;
using Stratis.SmartContracts.Core.State;
using Xunit;

namespace Stratis.Features.SystemContracts.Tests
{
    public class SystemContractRunnerTests
    {
        [Fact]
        public void Has_No_Dispatcher_StateIsTheSame()
        {
            var dispatchersMock = new Mock<IDispatcherRegistry>();

            var stateMock = new Mock<IStateRepositoryRoot>();
            var initialStateMock = new Mock<IStateRepository>();

            stateMock.Setup(s => s.StartTracking()).Returns(initialStateMock.Object);
            var contextMock = new Mock<ISystemContractTransactionContext>();
            contextMock.SetupGet(p => p.State).Returns(stateMock.Object);
            contextMock.SetupGet(p => p.CallData).Returns(new SystemContractCall(uint160.Zero, "", null));

            // We don't have the dispatcher.
            dispatchersMock.Setup(d => d.HasDispatcher(It.IsAny<uint160>())).Returns(false);

            var runner = new SystemContractRunner(dispatchersMock.Object);

            ISystemContractExecutionResult result = runner.Execute(contextMock.Object);

            stateMock.Verify(m => m.StartTracking(), Times.Once);

            // The state returned should be the same as what's returned after StartTracking is called
            Assert.Equal(initialStateMock.Object, result.NewState);
        }

        [Fact]
        public void Dispatch_Error_StateIsTheSame()
        {
            var dispatchersMock = new Mock<IDispatcherRegistry>();

            var stateMock = new Mock<IStateRepositoryRoot>();
            var initialStateMock = new Mock<IStateRepository>();

            stateMock.Setup(s => s.StartTracking()).Returns(initialStateMock.Object);
            var contextMock = new Mock<ISystemContractTransactionContext>();
            contextMock.SetupGet(p => p.State).Returns(stateMock.Object);
            contextMock.SetupGet(p => p.CallData).Returns(new SystemContractCall(uint160.Zero, "", null));

            var dispatcherMock = new Mock<IDispatcher>();
            dispatcherMock.Setup(m => m.Dispatch(It.IsAny<ISystemContractTransactionContext>())).Returns(Result.Fail("Error"));

            // We don't have the dispatcher.
            dispatchersMock.Setup(d => d.HasDispatcher(It.IsAny<uint160>())).Returns(true);
            dispatchersMock.Setup(d => d.GetDispatcher(It.IsAny<uint160>())).Returns(dispatcherMock.Object);

            var runner = new SystemContractRunner(dispatchersMock.Object);

            ISystemContractExecutionResult result = runner.Execute(contextMock.Object);

            stateMock.Verify(m => m.StartTracking(), Times.Once);

            dispatcherMock.Verify(d => d.Dispatch(contextMock.Object), Times.Once);

            // The state returned should be the same as what's returned after StartTracking is called
            Assert.Equal(initialStateMock.Object, result.NewState);
        }

        [Fact]
        public void Dispatch_Success_StateIsDifferent()
        {
            var dispatchersMock = new Mock<IDispatcherRegistry>();

            var stateMock = new Mock<IStateRepositoryRoot>();
            var initialStateMock = new Mock<IStateRepository>();

            stateMock.Setup(s => s.StartTracking()).Returns(initialStateMock.Object);
            var contextMock = new Mock<ISystemContractTransactionContext>();
            contextMock.SetupGet(p => p.State).Returns(stateMock.Object);
            contextMock.SetupGet(p => p.CallData).Returns(new SystemContractCall(uint160.Zero, "", null));

            var dispatcherMock = new Mock<IDispatcher>();

            // Dispatching successful
            dispatcherMock.Setup(m => m.Dispatch(It.IsAny<ISystemContractTransactionContext>())).Returns(Result.Ok());

            // We don't have the dispatcher.
            dispatchersMock.Setup(d => d.HasDispatcher(It.IsAny<uint160>())).Returns(true);
            dispatchersMock.Setup(d => d.GetDispatcher(It.IsAny<uint160>())).Returns(dispatcherMock.Object);

            var runner = new SystemContractRunner(dispatchersMock.Object);

            ISystemContractExecutionResult result = runner.Execute(contextMock.Object);

            stateMock.Verify(m => m.StartTracking(), Times.Once);

            dispatcherMock.Verify(d => d.Dispatch(contextMock.Object), Times.Once);

            // The state returned should be the same as what was on the context (now changed)
            Assert.Equal(stateMock.Object, result.NewState);
        }
    }
}
