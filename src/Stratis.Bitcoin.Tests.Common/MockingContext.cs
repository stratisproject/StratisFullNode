using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Stratis.Bitcoin.Tests.Common
{
    public static class IServiceCollectionExt
    {
        public static IServiceCollection AddSingleton<T>(this IServiceCollection serviceCollection, ConstructorInfo constructorInfo) where T : class
        {
            serviceCollection.AddSingleton(typeof(T), (s) => constructorInfo.Invoke(MockingContext.GetConstructorArguments(s, constructorInfo)));

            return serviceCollection;
        }
    }

    /// <summary>
    /// Implements a <c>GetService</c> that concretizes services on-demand and also mocks services that can't be otherwise concretized.
    /// </summary>
    public class MockingContext : IServiceProvider
    {
        private IServiceCollection serviceCollection;

        public MockingContext(IServiceCollection serviceCollection)
        {
            this.serviceCollection = serviceCollection;
        }

        /// <summary>
        /// Returns a concrete instance of the provided <paramref name="serviceType"/>.
        /// If the service type had no associated <c>AddService</c> then a mocked instance is returned.
        /// </summary>
        /// <param name="serviceType">The service type.</param>
        /// <returns>The service instance.</returns>
        /// <remarks><para>A mocked type can be passed in which case the mock object is returned for setup purposes.</para>
        /// <para>An enumerable type can be passed in which case multiple service instances are returned.</para></remarks>
        public object GetService(Type serviceType)
        {
            ServiceDescriptor[] services = this.serviceCollection.Where(s => s.ServiceType == serviceType).ToArray();

            // Need to do a bit more work to resolve IEnumerable types.
            if (typeof(IEnumerable<object>).IsAssignableFrom(serviceType))
            {
                Type elementType = serviceType.GetGenericArguments().First();
                Type collectionType = typeof(List<>).MakeGenericType(elementType);
                var collection = Activator.CreateInstance(collectionType);
                MethodInfo addMethod = collectionType.GetMethod("Add");
                foreach (ServiceDescriptor serviceDescriptor in services)
                {
                    var element = MakeConcrete(serviceDescriptor);
                    addMethod.Invoke(collection, new object[] { element });
                }

                return collection;
            }

            if (services.Length > 1)
                throw new InvalidOperationException($"There are {services.Length} services of type {serviceType}.");

            if (services.Length == 0)
                return MakeConcrete(serviceType);

            return MakeConcrete(services[0]);
        }

        private object MakeConcrete(ServiceDescriptor serviceDescriptor)
        {
            object service = serviceDescriptor.ImplementationInstance;
            if (service != null)
                return service;

            this.serviceCollection.Remove(serviceDescriptor);

            if (serviceDescriptor.ImplementationFactory != null)
            {
                service = serviceDescriptor.ImplementationFactory.Invoke(this);
                if (service != null)
                    this.serviceCollection.AddSingleton(serviceDescriptor.ServiceType, service);
                return service;
            }

            if (serviceDescriptor?.ImplementationType != null)
            {
                service = GetService(serviceDescriptor.ImplementationType);
                if (service != null && serviceDescriptor.ImplementationType != serviceDescriptor.ServiceType)
                    // Restore the service type singleton, with the resolved implementation instance.
                    this.serviceCollection.AddSingleton(serviceDescriptor.ServiceType, service);
                return service;
            }

            return MakeConcrete(serviceDescriptor.ServiceType);
        }

        private object MakeConcrete(Type serviceType)
        {
            object service;
            object mock = null;
            bool isMock = typeof(IMock<object>).IsAssignableFrom(serviceType);

            // Mock interfaces and explicit mock types.
            if (serviceType.IsInterface || isMock)
            {
                Type mockType = isMock ? serviceType : typeof(Mock<>).MakeGenericType(serviceType);
                if (isMock)
                    serviceType = serviceType.GetGenericArguments().First();
                
                if (serviceType.IsInterface)
                {
                    mock = Activator.CreateInstance(mockType);
                }
                else
                {
                    ConstructorInfo constructorInfo = GetConstructor(serviceType);
                    object[] args = GetConstructorArguments(this, constructorInfo);
                    mock = Activator.CreateInstance(mockType, args);
                }

                this.serviceCollection.AddSingleton(mockType, mock);

                // If we're mocking an interface then there is no separate singleton for the internal object.
                if (isMock && serviceType.IsInterface)
                    return mock;

                mock.SetPrivatePropertyValue("CallBase", true);

                service = ((dynamic)mock).Object;
            }
            else
            {
                ConstructorInfo constructorInfo = GetConstructor(serviceType);
                object[] args = GetConstructorArguments(this, constructorInfo);
                service = constructorInfo.Invoke(args);
            }

            this.serviceCollection.AddSingleton(serviceType, service);

            return isMock ? mock : service;
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

            // Default path.
            return serviceProvider.GetService(parameterInfo.ParameterType);
        }

        internal static object[] GetConstructorArguments(IServiceProvider serviceProvider, ConstructorInfo constructorInfo)
        {
            return constructorInfo.GetParameters().Select(p => ResolveParameter(serviceProvider, p)).ToArray();
        }
    }
}