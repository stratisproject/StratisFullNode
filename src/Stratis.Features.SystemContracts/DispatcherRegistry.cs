using System.Collections.Generic;
using System.Linq;
using NBitcoin;

namespace Stratis.Features.SystemContracts
{
    public interface IDispatcherRegistry
    {
        IDispatcher GetDispatcher(Identifier identifier);
        bool HasDispatcher(Identifier identifier);
    }

    /// <summary>
    /// Keeps track of the system contract dispatchers required for dynamic invocation.
    /// </summary>
    public class DispatcherRegistry : IDispatcherRegistry
    {
        private readonly Dictionary<Identifier, IDispatcher> dispatchers;

        // AuthDispatcher and DataDispatcher are registered with the DI container.
        public DispatcherRegistry(IEnumerable<IDispatcher> dispatchers)
        {
            this.dispatchers = dispatchers.ToDictionary(k => k.Identifier, v => v);
        }

        public bool HasDispatcher(Identifier identifier)
        {
            return this.dispatchers.ContainsKey(identifier);
        }

        public IDispatcher GetDispatcher(Identifier identifier)
        {
            return this.dispatchers[identifier];
        }
    }
}
