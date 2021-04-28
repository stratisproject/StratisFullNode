using System.Collections.Generic;
using NBitcoin;
using Stratis.Features.SystemContracts.Contracts;

namespace Stratis.Features.SystemContracts
{
    public interface IDispatcherRegistry
    {
        IDispatcher GetDispatcher(uint160 identifier);
        bool HasDispatcher(uint160 identifier);
    }

    /// <summary>
    /// Keeps track of the system contract dispatchers required for dynamic invocation.
    /// </summary>
    public class DispatcherRegistry : IDispatcherRegistry
    {
        private readonly Dictionary<uint160, IDispatcher> dispatchers;

        // AuthDispatcher and DataDispatcher are registered with the DI container.
        public DispatcherRegistry(AuthContract.Dispatcher authDispatcher, DataStorageContract.Dispatcher dataDispatcher)
        {
            this.dispatchers = new Dictionary<uint160, IDispatcher>
            {
                { AuthContract.Identifier, authDispatcher },
                { DataStorageContract.Identifier, dataDispatcher }
            };
        }

        public bool HasDispatcher(uint160 identifier)
        {
            return this.dispatchers.ContainsKey(identifier);
        }

        public IDispatcher GetDispatcher(uint160 identifier)
        {
            return this.dispatchers[identifier];
        }
    }
}
