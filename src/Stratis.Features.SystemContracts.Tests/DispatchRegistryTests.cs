using System.Collections.Generic;
using Moq;
using NBitcoin;
using Xunit;

namespace Stratis.Features.SystemContracts.Tests
{
    public class DispatchRegistryTests
    {
        [Fact]
        public void Identifiers_Are_Returned()
        {
            var identifierA = new Identifier(uint160.Zero);
            var dispatcherA = new Mock<IDispatcher>();
            dispatcherA.Setup(x => x.Identifier).Returns(identifierA);

            var identifierB = new Identifier(uint160.One);
            var dispatcherB = new Mock<IDispatcher>();
            dispatcherB.Setup(x => x.Identifier).Returns(identifierB);

            var dispatchers = new List<IDispatcher> { dispatcherA.Object, dispatcherB.Object };
            
            var registry = new DispatcherRegistry(dispatchers);

            Assert.Equal(dispatcherA.Object, registry.GetDispatcher(dispatcherA.Object.Identifier));
            Assert.Equal(dispatcherB.Object, registry.GetDispatcher(dispatcherB.Object.Identifier));
        }
    }
}
