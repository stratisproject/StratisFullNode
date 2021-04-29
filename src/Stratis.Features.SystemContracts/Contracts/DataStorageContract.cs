using System;
using System.Linq;
using System.Text;
using CSharpFunctionalExtensions;
using NBitcoin;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts.Contracts
{
    /// <summary>
    /// Sample contract that uses auth and stores data in the state.
    /// </summary>
    public class DataStorageContract
    {
        public DataStorageContract(IStateRepositoryRoot state, Network network, AuthContract auth)
        {
            this.State = state;
            this.Network = network;
            this.Auth = auth;

            if (!this.Initialized)
            {
                this.State.SetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes("Network"), Encoding.UTF8.GetBytes(network.Name));
                this.Initialized = true;
            }
        }

        public bool Initialized
        {
            get
            {
                var data = this.State.GetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes("Initialized"));
                return data == null ? false : BitConverter.ToBoolean(data);
            }

            set
            {
                this.State.SetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes("Initialized"), BitConverter.GetBytes(value));
            }
        }

        /// <summary>
        /// Example of a unique identifier, which we need to fit in a uint160 somehow. We can change this.
        /// </summary>
        public static Identifier Identifier => new Identifier(new uint160(SCL.Crypto.SHA3.Keccak256(Encoding.UTF8.GetBytes(nameof(DataStorageContract))).Take(20).ToArray()));

        public IStateRepositoryRoot State { get; }

        public Network Network { get; }

        public AuthContract Auth { get; }

        public bool AddData(string[] signatories, string key, string value)
        {
            if (!this.Auth.IsAuthorised(signatories))
                return false;

            this.State.SetStorageValue(Identifier.Data, Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(value));

            return true;
        }

        public class Dispatcher : IDispatcher<DataStorageContract>
        {
            private readonly Network network;
            private readonly IDispatcher<AuthContract> authContract;

            public Dispatcher(Network network, IDispatcher<AuthContract> authContract)
            {
                this.network = network;
                this.authContract = authContract;
            }

            public Identifier Identifier => DataStorageContract.Identifier;

            public DataStorageContract GetInstance(ISystemContractTransactionContext context)
            {
                return new DataStorageContract(context.State, this.network, this.authContract.GetInstance(context));
            }

            /// <summary>
            /// Instantiates the type, finds the method to call and dispatches the call.
            /// </summary>
            /// <param name="context"></param>
            /// <returns>A result indicating whether or not the execution was successful.</returns>
            public Result<object> Dispatch(ISystemContractTransactionContext context)
            {
                DataStorageContract instance = GetInstance(context);
                
                switch (context.CallData.MethodName)
                {
                    case nameof(DataStorageContract.AddData):
                        var result = instance.AddData(context.CallData.Parameters[0] as string[], context.CallData.Parameters[1] as string, context.CallData.Parameters[2] as string);
                        return Result.Ok<object>(result);
                    default:
                        return Result.Fail<object>($"Method {context.CallData.MethodName} does not exist on type {nameof(DataStorageContract)} v{context.CallData.Version}");
                }
            }
        }
    }
}
