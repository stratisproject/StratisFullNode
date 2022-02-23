using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stratis.Bitcoin.Utilities;

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
            GetOrAddService(serviceType);

            Type mockType = MockType(serviceType);
            return this.serviceCollection.SingleOrDefault(s => s.ServiceType == mockType)?.ImplementationInstance;
        }

        public Mock<T> GetMock<T>() where T : class
        {
            Guard.Assert(typeof(T).IsInterface);

            return (Mock<T>)GetMock(typeof(T));
        }

        private object GetOrAddService(Type serviceType, Type implementationType = null, ConstructorInfo constructorInfo = null, bool allowAdd = true)
        {            
            var service = this.serviceCollection
                .Where(s => s.ServiceType == serviceType && (implementationType == null || s.ImplementationType == implementationType))
                .SingleOrDefault()?.ImplementationInstance;

            if (service != null || !allowAdd)
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
                constructorInfo ??= implementationType.GetConstructors().Single();
                object[] args = constructorInfo.GetParameters().Select(p => GetOrAddService(p.ParameterType)).ToArray();
                service = Activator.CreateInstance(implementationType, args);
            }

            this.serviceCollection.AddSingleton(serviceType, service);

            return service;
        }

        public T GetService<T>(Type implementationType = null, bool addIfNotExists = false) where T : class
        {
            return (T)GetOrAddService(typeof(T), implementationType, allowAdd: addIfNotExists);
        }

        public MockingContext AddService<T>() where T : class
        {
            Guard.Assert(typeof(T).IsInterface);

            GetOrAddService(typeof(T));
            return this;
        }

        public MockingContext AddService<T>(ConstructorInfo constructorInfo) where T : class
        {
            GetOrAddService(typeof(T), constructorInfo.DeclaringType, constructorInfo);
            return this;
        }

        public MockingContext AddService<T>(Type implementationType) where T : class
        {
            GetOrAddService(typeof(T), implementationType);
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