using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.RuntimeObserver;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Defines the way the system contract state transitions are handled.
    /// 
    /// System contracts operate by receiving call transactions on-chain as per usual. System contracts are not deployable on-chain and instances thereof are not instantiated from bytecode.
    /// 
    /// Instead they are instantiated from first-class types and form a part of the core full node codebase.
    /// 
    /// There are other subtle differences too:
    /// - State root is the same
    /// - Contracts are not accessed by "addresses"
    /// - Transferring funds is currently blocked
    /// - No concept of gas, contracts are assumed to be halting within a specific time period
    /// - No receipts
    /// - Whitelisting is done by BIP9 activations
    /// - We don't need to use ISmartContractState within a contract if we don't want to. 
    ///   Can use some other way of getting an instance of a contract that has the dependencies it needs. 
    ///   It's still useful to have IBlock, IMessage to get the chain state but again not necessary because you can also get those from the chainindexer or wherever so why lock people in.
    /// 
    /// Things we want to reuse from existing SC implementation:
    /// - ContractTxData + Opcodes for call
    /// - State transitions to apply the messages
    /// - The state root implementation
    /// - The account state implementation?
    /// </summary>
    public class SystemContractStateProcessor : IStateProcessor
    {
        public StateTransitionResult Apply(IState state, ExternalCreateMessage message)
        {
            // Return VmError to indicate a failure.
            return StateTransitionResult.Fail((Gas)0, StateTransitionErrorKind.VmError);
        }

        public StateTransitionResult Apply(IState state, InternalCreateMessage message)
        {
            return StateTransitionResult.Fail((Gas)0, StateTransitionErrorKind.VmError);
        }

        public StateTransitionResult Apply(IState state, InternalCallMessage message)
        {
            return StateTransitionResult.Fail((Gas)0, StateTransitionErrorKind.VmError);
        }

        public StateTransitionResult Apply(IState state, ExternalCallMessage message)
        {
            // This needs to happen after the base fee is charged, which is why it's in here.

            if (message.Method.Name == null)
            {
                return StateTransitionResult.Fail((Gas)0, StateTransitionErrorKind.NoMethodName);
            }

            string type = state.ContractState.GetContractType(message.To);

            VmExecutionResult result = null; // TODO invoke contract here

            bool revert = !result.IsSuccess;

            if (revert)
            {
                return StateTransitionResult.Fail(
                    (Gas)0,
                    result.Error);
            }

            return StateTransitionResult.Ok(
                (Gas)0,
                message.To,
                result.Success.Result
            );
        }

        public StateTransitionResult Apply(IState state, ContractTransferMessage message)
        {
            return StateTransitionResult.Fail((Gas)0, StateTransitionErrorKind.VmError);
        }
    }
}