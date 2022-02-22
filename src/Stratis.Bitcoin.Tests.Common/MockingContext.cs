using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Stratis.Bitcoin.Tests.Common
{
    public class MockingContext : IDisposable
    {
        private IServiceCollection serviceCollection;

        public MockingContext() : this(new ServiceCollection())
        {
        }

        public MockingContext(IServiceCollection serviceCollection)
        {
            this.serviceCollection = serviceCollection;
        }

        private Type MockType(Type serviceType) => typeof(Mock<>).MakeGenericType(serviceType);

        private object GetMock(Type serviceType)
        {
            Type mockType = MockType(serviceType);
            return this.serviceCollection.SingleOrDefault(s => s.ServiceType == mockType)?.ImplementationInstance;
        }

        public Mock<T> GetMock<T>() where T : class
        {
            return (Mock<T>)GetMock(typeof(T));
        }

        private object GetService(Type serviceType, Type implementationType = null)
        {
            var service = this.serviceCollection.SingleOrDefault(s => s.ServiceType == serviceType)?.ImplementationInstance;
            if (service != null)
                return service;

            implementationType ??= serviceType;

            if (implementationType.IsInterface)
            {
                Type mockType = MockType(implementationType);
                var mock = Activator.CreateInstance(mockType);
                service = ((dynamic)mock).Object;
                this.serviceCollection.AddSingleton(mockType, mock);
            }
            else
            {
                object[] args = implementationType.GetConstructors().Single().GetParameters().Select(p => GetService(p.ParameterType)).ToArray();
                service = Activator.CreateInstance(implementationType, args);
            }

            this.serviceCollection.AddSingleton(serviceType, service);

            return service;
        }

        public T GetService<T>(Type implementationType = null) where T : class
        {
            return (T)GetService(typeof(T), implementationType);
        }

        public MockingContext AddService<T>() where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T));
            return this;
        }

        public MockingContext AddService<T>(T implementationInstance) where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T), implementationInstance);
            return this;
        }

        public void Dispose()
        {
            this.serviceCollection.Clear();
        }
    }
}