using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Tests.Common
{
    public class MockingContext : IServiceProvider
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

        public T GetService<T>() where T : class
        {
            return (T)GetOrAddService(typeof(T));
        }

        public object GetService(Type serviceType)
        {
            return GetOrAddService(serviceType);
        }

        public MockingContext AddService<T>(ConstructorInfo constructorInfo) where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T), (s) => constructorInfo.Invoke(GetConstructorArguments(s, constructorInfo)));
            return this;
        }

        public MockingContext AddService<T>(Type implementationType) where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T), implementationType);
            return this;
        }

        public MockingContext AddService<T>(T implementationInstance) where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T), implementationInstance);
            return this;
        }

        public MockingContext AddService<T>(Func<IServiceProvider, T> implementationFactory) where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T), (s) => implementationFactory(s));
            return this;
        }

        private ServiceDescriptor[] FindServices(Type serviceType, Type implementationType = null)
        {
            return this.serviceCollection.Where(s => s.ServiceType == serviceType && (implementationType == null || s.ImplementationType == implementationType)).ToArray();
        }

        private object GetOrAddService(Type serviceType, Type implementationType = null, ConstructorInfo constructorInfo = null)
        {
            ServiceDescriptor[] services = FindServices(serviceType, implementationType);

            if (services.Length > 1)
                throw new InvalidOperationException($"There are {services.Length} services of type {serviceType}.");

            var service = services.FirstOrDefault()?.ImplementationInstance;

            if (service != null)
                return service;

            if (services.Length == 1)
            {
                implementationType = services[0].ImplementationType;
                service = services[0].ImplementationFactory?.Invoke(this);
                implementationType ??= service?.GetType();
                this.serviceCollection.Remove(services[0]);
            }

            implementationType ??= serviceType;

            if (implementationType.IsInterface)
            {
                Type mockType = MockType(implementationType);
                var mock = Activator.CreateInstance(mockType);
                service = ((dynamic)mock).Object;
                this.serviceCollection.AddSingleton(mockType, mock);
            }
            else if (service == null)
            {
                constructorInfo ??= GetConstructor(implementationType);
                object[] args = GetConstructorArguments(this, constructorInfo);
                service = constructorInfo.Invoke(args);
            }

            this.serviceCollection.AddSingleton(serviceType, service);

            return service;
        }

        private static ConstructorInfo GetConstructor(Type implementationType)
        {
            ConstructorInfo[] constructors = implementationType.GetConstructors();

            // If there is a choice of two then ignore the parameterless constructor.
            if (constructors.Length == 2)
                constructors = constructors.Where(c => c.GetParameters().Length != 0).ToArray();

            // Too much ambiguity.
            if (constructors.Length != 1)
                throw new InvalidOperationException($"There are {constructors.Length} constructors for {implementationType}.");

            return constructors.Single();
        }

        private static object ResolveParameter(IServiceProvider serviceProvider, ParameterInfo parameterInfo)
        {
            // Some constructors have value type arguments with default values.
            if (parameterInfo.ParameterType.IsValueType)
                return parameterInfo.DefaultValue;
            
            // Need to do a bit more work to inject IEnumerable parameters.
            if (parameterInfo.ParameterType.GetInterface("IEnumerable") != null)
            {
                Type elementType = parameterInfo.ParameterType.GetGenericArguments().First();
                Type collectionType = typeof(List<>).MakeGenericType(elementType);
                var collection = Activator.CreateInstance(collectionType);
                MockingContext mockingContext = serviceProvider as MockingContext;
                MethodInfo addMethod = collectionType.GetMethod("Add");
                foreach (var element in mockingContext.FindServices(elementType).Select(s => s.ImplementationInstance).Where(i => i != null))
                {
                    addMethod.Invoke(collection, new object[] { element });
                }
                return collection;
            }

            // Default path.
            return serviceProvider.GetService(parameterInfo.ParameterType);
        }

        private static object[] GetConstructorArguments(IServiceProvider serviceProvider, ConstructorInfo constructorInfo)
        {
            return constructorInfo.GetParameters().Select(p => ResolveParameter(serviceProvider, p)).ToArray();
        }
    }
}