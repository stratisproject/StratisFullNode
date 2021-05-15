using CSharpFunctionalExtensions;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface ISystemContractRunner
    {
        ISystemContractRunnerResult Execute(ISystemContractTransactionContext context);
    }

    /// <summary>
    /// Dispatches a call to a system contract.
    /// 
    /// Starts with the transaction context, which contains the state root of the chain 
    /// after the previous block (or after the last transaction in the current block) was executed.
    /// 
    /// We use the dispatcher registry to dynamically resolve the contract instance and dispatch the method call.
    /// 
    /// The result of the call returns the updated state root, which may be the same if nothing changed.
    /// 
    /// If the execution was not successful, <see cref="ISystemContractRunnerResult.Result"/> will be null.
    /// 
    /// If the execution was successful, <see cref="ISystemContractRunnerResult.Result"/> will be the result of the execution,
    /// or <see cref="DispatchResult.Void"/> if the method called is a void.
    /// </summary>
    public class SystemContractRunner : ISystemContractRunner
    {
        private readonly IDispatcherRegistry dispatcherRegistry;

        public SystemContractRunner(IDispatcherRegistry dispatchers)
        {
            this.dispatcherRegistry = dispatchers;
        }

        public ISystemContractRunnerResult Execute(ISystemContractTransactionContext context)
        {
            IStateRepositoryRoot state = context.State;

            // Create a new copy of the initial state that we can return if we need to ignore the changes made.
            var initialRoot = state.Root;

            // Find the dispatcher.
            if (!this.dispatcherRegistry.HasDispatcher(context.CallData.Identifier))
            {
                // Return the same state.
                return new SystemContractRunnerResult(state);
            }

            IDispatcher dispatcher = this.dispatcherRegistry.GetDispatcher(context.CallData.Identifier);

            // Invoke the contract.
            Result<object> executionResult = dispatcher.Dispatch(context);

            if (executionResult.IsFailure)
            {
                // Return to the root state.
                state.SyncToRoot(initialRoot);

                return new SystemContractRunnerResult(state);
            }

            // Return new state.
            return new SystemContractRunnerResult(state, executionResult.Value);
        }
    }
}
