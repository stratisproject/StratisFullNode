using Moq;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;
using Xunit;

namespace Stratis.Features.SystemContracts.Tests
{
    public class SystemContractExecutorTests
    {
        [Fact]
        public void Test()
        {

        }
    }

    public class SystemContractExecutorFactoryTests
    {
        [Fact]
        public void Should_Return_Executor()
        {
            var factory = new TypeExecutorFactory();
            IContractExecutor executor = factory.CreateExecutor(Mock.Of<IStateRepositoryRoot>(), Mock.Of<IContractTransactionContext>());

            Assert.NotNull(executor);
        }
    }
}
