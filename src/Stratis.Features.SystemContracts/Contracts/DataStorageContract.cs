using System;
using System.Linq;
using System.Text;
using CSharpFunctionalExtensions;
using NBitcoin;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts.Contracts
{
    public class DataStorageContract
    {
        public DataStorageContract(IStateRepositoryRoot state, Network network, AuthContract auth)
        {
            this.State = state;
            this.Network = network;
            this.Auth = auth;

            if (!this.Initialized)
            {
                this.State.SetStorageValue(Identifier, Encoding.UTF8.GetBytes("Network"), Encoding.UTF8.GetBytes(network.Name));
                this.Initialized = true;
            }
        }

        public bool Initialized
        {
            get { return BitConverter.ToBoolean(this.State.GetStorageValue(DataStorageContract.Identifier, Encoding.UTF8.GetBytes("Initialized"))); }
            set { this.State.SetStorageValue(Identifier, Encoding.UTF8.GetBytes("Initialized"), BitConverter.GetBytes(value)); }
        }

        /// <summary>
        /// Example of a unique identifier, which we need to fit in a uint160 somehow. We can change this.
        /// </summary>
        public static uint160 Identifier => new uint160(SCL.Crypto.SHA3.Keccak256(Encoding.UTF8.GetBytes(nameof(DataStorageContract))).Take(20).ToArray());

        public IStateRepositoryRoot State { get; }

        public Network Network { get; }

        public AuthContract Auth { get; }

        public void AddData(string[] signatories, string key, string value)
        {
            if (!this.Auth.IsAuthorised(signatories))
                return;

            this.State.SetStorageValue(Identifier, Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));
        }

        /// <summary>
        /// Dynamic method resolution and dependency injection for the <see cref="DataStorageContract"/> type
        /// to allow it to be invoked on-chain.
        /// 
        /// Why is this necessary?
        /// For system contracts, call data is received from a transaction and deserialized to strings.
        /// Use reflection to attempt to find a matching method overload for these strings is slow and complicated.
        /// It's much easier to define how to dispatch the method call here.
        /// 
        /// Using a concrete type (rather than just a Dispatch method) also allows us to
        /// use the dependency injection framework to pass in any dependencies needed when instantiating the
        /// contract, because we can register SimpleContract.Dispatcher with the DI container.
        /// 
        /// We only need to implement this class if the system contract is called on-chain. Otherwise it's not needed.
        /// </summary>
        public class Dispatcher : IDispatcher<DataStorageContract>
        {
            private readonly Network network;
            private readonly SystemContractContainer systemContractContainer;
            private readonly IDispatcher<AuthContract> authContract;

            public Dispatcher(Network network, SystemContractContainer systemContractContainer, IDispatcher<AuthContract> authContract)
            {
                this.network = network;
                this.systemContractContainer = systemContractContainer;
                this.authContract = authContract;
            }

            public uint160 Identifier => DataStorageContract.Identifier;

            public DataStorageContract GetInstance(ISystemContractTransactionContext context)
            {
                return new DataStorageContract(context.State, this.network, this.authContract.GetInstance(context));
            }

            /// <summary>
            /// Instantiates the type, finds the method to call and dispatches the call.
            /// </summary>
            /// <param name="context"></param>
            /// <returns>A result indicating whether or not the execution was successful.</returns>
            public Result Dispatch(ISystemContractTransactionContext context)
            {
                DataStorageContract instance = GetInstance(context);

                switch (context.CallData.MethodName)
                {
                    case nameof(DataStorageContract.AddData):
                        instance.AddData(context.CallData.Parameters[0] as string[], context.CallData.Parameters[1] as string, context.CallData.Parameters[2] as string);
                        return Result.Ok();
                    default:
                        return Result.Fail($"Method {context.CallData.MethodName} does not exist on type {nameof(DataStorageContract)} v{context.CallData.Version}");
                }
            }
        }
    }
}
