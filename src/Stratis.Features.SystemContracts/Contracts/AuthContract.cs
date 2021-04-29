using System;
using System.Linq;
using System.Text;
using CSharpFunctionalExtensions;
using NBitcoin;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts.Contracts
{
    public class AuthContract
    {
        public AuthContract(IStateRepositoryRoot state, ISystemContractContainer systemContractContainer)
        {
            this.State = state;
            this.SystemContractContainer = systemContractContainer;

            if (!this.Initialized)
            {
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

        public static Identifier Identifier => new Identifier(new uint160(SCL.Crypto.SHA3.Keccak256(Encoding.UTF8.GetBytes(nameof(AuthContract))).Take(20).ToArray()));

        public IStateRepositoryRoot State { get; }

        public ISystemContractContainer SystemContractContainer { get; }

        public Network Network { get; }

        public bool IsAuthorised(string[] signatures)
        {
            return VerifySignatories(signatures, this.SystemContractContainer.PrimaryAuthenticators?.Signatories ?? new string[] { });
        }

        private bool VerifySignatories(string[] signatures, string[] signatories)
        {
            if(signatures.Length == 0)
                return false;

            return signatures[0] == "secret";
        }

        public class Dispatcher : IDispatcher<AuthContract>
        {
            private readonly ISystemContractContainer systemContractContainer;

            public Dispatcher(ISystemContractContainer systemContractContainer)
            {
                this.systemContractContainer = systemContractContainer;
            }

            public Identifier Identifier => AuthContract.Identifier;

            public Result<object> Dispatch(ISystemContractTransactionContext context)
            {
                AuthContract instance = GetInstance(context);

                switch (context.CallData.MethodName)
                {
                    case nameof(AuthContract.IsAuthorised):
                        var result = instance.IsAuthorised(context.CallData.Parameters[0] as string[]);
                        return Result.Ok<object>(result);
                    default:
                        return Result.Fail<object>($"Method {context.CallData.MethodName} does not exist on type {nameof(AuthContract)} v{context.CallData.Version}");
                }
            }

            public AuthContract GetInstance(ISystemContractTransactionContext context)
            {
                return new AuthContract(context.State, this.systemContractContainer);
            }
        }
    }
}
