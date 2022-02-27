using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Stratis.Bitcoin.Tests.Common
{
    /// <summary>
    /// Concretizes services recursively and also mocks services that can't be otherwise concretized.
    /// </summary>
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

        /// <summary>
        /// Adds a singleton to the underlying service collection.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="implementationType">The implementation type.</param>
        /// <returns>The <c>this</c> instance for fluent calls.</returns>
        public MockingContext AddService<T>(Type implementationType = null) where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T), implementationType ?? typeof(T));
            return this;
        }

        /// <summary>
        /// Adds a singleton to the underlying service collection.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="implementationInstance">The implementation instance.</param>
        /// <returns>The <c>this</c> instance for fluent calls.</returns>
        public MockingContext AddService<T>(T implementationInstance) where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T), implementationInstance);
            return this;
        }

        /// <summary>
        /// Adds a singleton to the underlying service collection.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="implementationFactory">The implementation factory.</param>
        /// <returns>The <c>this</c> instance for fluent calls.</returns>
        public MockingContext AddService<T>(Func<IServiceProvider, T> implementationFactory) where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T), (s) => implementationFactory(s));
            return this;
        }

        /// <summary>
        /// Adds a singleton to the underlying service collection.
        /// </summary>
        /// <typeparam name="T">The service type.</typeparam>
        /// <param name="constructorInfo">The constructor info.</param>
        /// <returns>The <c>this</c> instance for fluent calls.</returns>
        public MockingContext AddService<T>(ConstructorInfo constructorInfo) where T : class
        {
            this.serviceCollection.AddSingleton(typeof(T), (s) => constructorInfo.Invoke(GetConstructorArguments(s, constructorInfo)));
            return this;
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
            if (typeof(IMock<object>).IsAssignableFrom(serviceType))
            {
                Type mockType = serviceType;

                serviceType = serviceType.GetGenericArguments().First();

                if (!this.serviceCollection.Any(s => s.ServiceType == serviceType))
                    MakeConcrete(serviceType, mockIt: true);

                return this.serviceCollection.SingleOrDefault(s => s.ServiceType == mockType)?.ImplementationInstance;
            }

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
                    var element = MakeConcrete(serviceType, serviceDescriptor);
                    addMethod.Invoke(collection, new object[] { element });
                }

                return collection;
            }

            if (services.Length > 1)
                throw new InvalidOperationException($"There are {services.Length} services of type {serviceType}.");

            return MakeConcrete(serviceType, services.FirstOrDefault());
        }

        private object MakeConcrete(Type serviceType, ServiceDescriptor serviceDescriptor = null, bool mockIt = false)
        {
            object service = serviceDescriptor?.ImplementationInstance;
            if (service != null)
                return service;

            this.serviceCollection.Remove(serviceDescriptor);

            if (serviceDescriptor?.ImplementationFactory != null)
            {
                service = serviceDescriptor.ImplementationFactory.Invoke(this);
                if (service != null)
                {
                    this.serviceCollection.AddSingleton(serviceType, service);
                    return service;
                }
            }

            Type implementationType = serviceDescriptor?.ImplementationType ?? serviceType;

            if (implementationType.IsInterface || mockIt)
            {
                Type mockType = typeof(Mock<>).MakeGenericType(implementationType);
                object mock;
                if (implementationType.IsInterface)
                {
                    mock = Activator.CreateInstance(mockType);
                }
                else
                {
                    ConstructorInfo constructorInfo = GetConstructor(implementationType);
                    object[] args = GetConstructorArguments(this, constructorInfo);
                    mock = Activator.CreateInstance(mockType, args);
                }
                service = ((dynamic)mock).Object;
                this.serviceCollection.AddSingleton(mockType, mock);
            }
            else
            {
                ConstructorInfo constructorInfo = GetConstructor(implementationType);
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

            // Default path.
            return serviceProvider.GetService(parameterInfo.ParameterType);
        }

        private static object[] GetConstructorArguments(IServiceProvider serviceProvider, ConstructorInfo constructorInfo)
        {
            return constructorInfo.GetParameters().Select(p => ResolveParameter(serviceProvider, p)).ToArray();
        }
    }
}