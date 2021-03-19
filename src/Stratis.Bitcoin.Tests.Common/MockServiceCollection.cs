using System;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.Util;

namespace Stratis.Bitcoin.Tests.Common
{
    public class MockServiceCollection
    {
        private IServiceCollection serviceCollection;
        private IServiceProvider serviceProvider;

        public object GetService(Type type)
        {
            object service = this.serviceProvider.GetService(type);
            if (service != null)
                return service;

            this.serviceCollection.AddSingleton(type, (serviceProvider) => ActivatorUtilities.CreateInstance(serviceProvider, type));
            this.serviceProvider = this.serviceCollection.BuildServiceProvider();
            
            return this.serviceProvider.GetService(type);
        }

        public MockServiceCollection()
        {
            this.serviceCollection = new ServiceCollection();
            this.serviceCollection.AddSingleton(typeof(ICallDataSerializer), new Mock<ICallDataSerializer>().Object);
            this.serviceCollection.AddSingleton(typeof(ISenderRetriever), new Mock<ISenderRetriever>().Object);
            this.serviceCollection.AddSingleton(typeof(IStateRepositoryRoot), new Mock<IStateRepositoryRoot>().Object);
            this.serviceProvider = this.serviceCollection.BuildServiceProvider();
        }
    }
}
