﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NBitcoin;

namespace Stratis.Bitcoin.Interfaces
{
    /// <summary>
    /// A reader for extracting an address from a Script
    /// </summary>
    public interface IScriptAddressReader
    {
        /// <summary>
        /// Extracts an address from a given Script, if available. Otherwise returns <see cref="string.Empty"/>
        /// </summary>
        /// <param name="scriptTemplate">The appropriate template for this type of script.</param>
        /// <param name="network">The network.</param>
        /// <param name="script">The script.</param>
        /// <returns></returns>
        string GetAddressFromScriptPubKey(ScriptTemplate scriptTemplate, Network network, Script script);

        IEnumerable<TxDestination> GetDestinationFromScriptPubKey(ScriptTemplate scriptTemplate, Script script);
    }

    public static class IServiceCollectionExt
    {
        /// <summary>
        /// Replaces a service and provides a factory for creating a new instance that chains to the previous implementation.
        /// </summary>
        /// <typeparam name="I">The service type.</typeparam>
        /// <param name="services">The services collection.</param>
        /// <param name="factory">The factory used to create a new instance that chains to the previous implementation.</param>
        /// <returns></returns>
        public static IServiceCollection Replace<I>(this IServiceCollection services, Func<IServiceProvider, I, I> factory)
        {
            ServiceDescriptor previous = services.LastOrDefault(s => s.ServiceType.IsAssignableFrom(typeof(I)));

            services.Replace(new ServiceDescriptor(typeof(I), provider =>
            {
                var old = (I)(previous.ImplementationInstance ?? (previous.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, previous.ImplementationType)));

                var instance = factory(provider, old);

                return instance;
            }, ServiceLifetime.Singleton));

            return services;
        }
    }
}
