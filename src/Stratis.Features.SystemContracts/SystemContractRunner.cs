using System.Linq;
using System.Text;
using NBitcoin;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public class SimpleContract
    {
        public SimpleContract(IStateRepositoryRoot state, Network network)
        {
            this.State = state;
            this.Network = network;
            this.State.SetStorageValue(this.Identifier, Encoding.UTF8.GetBytes("Network"), Encoding.UTF8.GetBytes(network.Name));
        }

        /// <summary>
        /// Example of a unique identifier, which we need to fit in a uint160 somehow.
        /// </summary>
        public uint160 Identifier => new uint160(SCL.Crypto.SHA3.Keccak256(Encoding.UTF8.GetBytes(nameof(SimpleContract))).Take(20).ToArray());

        public IStateRepositoryRoot State { get; }

        public Network Network { get; }

        public void ModifyState(string key, string value)
        {
            this.State.SetStorageValue(this.Identifier, Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// Dynamic method resolution and dependency injection for the <see cref="SimpleContract"/> type.
        /// 
        /// Why is this necessary?
        /// For system contracts, call data is received from a transaction and deserialized to strings.
        /// Use reflection to attempt to find a matching method overload for these strings is slow and complicated.
        /// It's much easier to define how to dispatch the method call here. This also allows us to
        /// use the dependency injection framework to pass in any dependencies needed when instantiating the
        /// contract, because we can register SimpleContract.Dispatcher with the DI container.
        /// </summary>
        public class Dispatcher
        {
            public Dispatcher(Network network)
            {
                this.Network = network;
            }

            public Network Network { get; }

            public void Dispatch(SystemContractTransactionContext context)
            {
                var instance = new SimpleContract(context.State, this.Network);

                switch(context.CallData.MethodName)
                {
                    case nameof(SimpleContract.ModifyState):
                        instance.ModifyState(context.CallData.Parameters[0] as string, context.CallData.Parameters[1] as string);
                        return;
                    default:
                        return;
                }
            }
        }
    }

    public class SystemContractRunner // TODO rename this is just to avoid conflicts
    {
        public ISystemContractExecutionResult Execute(SystemContractTransactionContext context)
        {
            var state = context.State;

            // Instantiate the contract for the type and version
            // Inject any dependencies it may have
            // Invoke the method
            


            // TODO return new state
            return new SystemContractExecutionResult(null);
        }
    }
}
