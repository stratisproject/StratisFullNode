using System.Collections.Generic;
using System.Linq;
using NBitcoin;

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
        public DispatcherRegistry(IEnumerable<IDispatcher> dispatchers)
        {
            this.dispatchers = dispatchers.ToDictionary(k => k.Identifier, v => v);
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
