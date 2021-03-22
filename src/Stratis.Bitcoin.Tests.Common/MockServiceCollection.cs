using System;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Stratis.Bitcoin.Tests.Common
{
    public class MockServiceCollection : ServiceCollection
    {
        private IServiceProvider serviceProvider;

        public object GetService(Type type)
        {
            object service = this.serviceProvider.GetService(type);
            if (service != null)
                return service;

            this.AddSingleton(type, (serviceProvider) => ActivatorUtilities.CreateInstance(serviceProvider, type));

            this.serviceProvider = this.BuildServiceProvider();
            
            return this.serviceProvider.GetService(type);
        }

        public T GetService<T>()
        {
            return (T)GetService(typeof(T));
        }

        public void Configure(Action<MockServiceCollection> services = null)
        {
            services?.Invoke(this);
            this.serviceProvider = this.BuildServiceProvider();
        }

        public MockServiceCollection AddMockSingleton(Type type)
        {
            Type mockType = typeof(Mock<>).MakeGenericType(type);
            Mock mockObject = (Mock)Activator.CreateInstance(mockType);
            this.AddSingleton(type, mockObject.Object);
            return this;
        }

        public MockServiceCollection AddMockSingleton<T>()
        {
            return AddMockSingleton(typeof(T));
        }

        public MockServiceCollection(Action<MockServiceCollection> services = null)
        {
            this.Configure(services);
        }
    }
}
