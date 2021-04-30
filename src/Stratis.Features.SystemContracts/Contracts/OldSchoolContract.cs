using System.Linq;
using System.Text;
using CSharpFunctionalExtensions;
using NBitcoin;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Metering;
using Stratis.SmartContracts.RuntimeObserver;

namespace Stratis.Features.SystemContracts.Contracts
{
    /// <summary>
    /// Example of how we can still write a <see cref="SmartContract"/> that uses <see cref="ISmartContractState"/> if we want.
    /// </summary>
    public class OldSchoolContract : SmartContract
    {
        public OldSchoolContract(ISmartContractState contractState) 
            : base(contractState)
        {            
            if (!this.Initialized)
            {
                this.Initialized = true;
            }
        }

        public static Identifier Identifier => new Identifier(new uint160(SCL.Crypto.SHA3.Keccak256(Encoding.UTF8.GetBytes(nameof(OldSchoolContract))).Take(20).ToArray()));

        public bool Initialized 
        { 
            get => this.State.GetBool(nameof(this.Initialized));
            set => this.State.SetBool(nameof(this.Initialized), value);
        }

        public class Dispatcher : IDispatcher<OldSchoolContract>
        {
            private readonly ISmartContractStateFactory contractStateFactory;
            private readonly IStateFactory stateFactory;

            public Dispatcher(ISmartContractStateFactory contractStateFactory, IStateFactory stateFactory)
            {
                this.contractStateFactory = contractStateFactory;
                this.stateFactory = stateFactory;
            }

            public Identifier Identifier => OldSchoolContract.Identifier;

            public Result<object> Dispatch(ISystemContractTransactionContext context)
            {
                OldSchoolContract instance = GetInstance(context);

                switch (context.CallData.MethodName)
                {
                    case nameof(Initialized):
                        var result = instance.Initialized;
                        return Result.Ok<object>(result);
                    default:
                        return Result.Fail<object>($"Method {context.CallData.MethodName} does not exist on type {nameof(OldSchoolContract)} v{context.CallData.Version}");
                }
            }

            public OldSchoolContract GetInstance(ISystemContractTransactionContext context)
            {
                var gasMeter = new GasMeter((Gas)0);

                IState state = this.stateFactory.Create(context.State, new SmartContracts.Block(context.BlockHeight, context.Coinbase.ToAddress()), 0, context.Transaction.GetHash());
                ISmartContractState contractState = this.contractStateFactory.Create(state, gasMeter, this.Identifier.Data, context.Message, context.State);
                return new OldSchoolContract(contractState);
            }
        }
    }
}
