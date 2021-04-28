using System.Collections.Generic;
using NBitcoin;

namespace Stratis.Features.SystemContracts
{
    public class Dispatchers
    {
        private readonly Dictionary<uint160, IDispatcher> dispatchers;

        // AuthDispatcher and DataDispatcher are registered with the DI container.
        public Dispatchers(AuthContract.Dispatcher authDispatcher, DataStorageContract.Dispatcher dataDispatcher)
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
