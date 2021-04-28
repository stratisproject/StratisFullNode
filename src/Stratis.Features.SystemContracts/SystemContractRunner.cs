using CSharpFunctionalExtensions;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public class SystemContractRunner
    {
        private readonly IDispatcherRegistry dispatcherRegistry;

        public SystemContractRunner(IDispatcherRegistry dispatchers)
        {
            this.dispatcherRegistry = dispatchers;
        }

        public ISystemContractExecutionResult Execute(ISystemContractTransactionContext context)
        {
            // Create a new copy of the initial state that we can return if we need to ignore the changes made.
            IStateRepository initialState = context.State.StartTracking();

            IStateRepositoryRoot state = context.State;

            // Find the dispatcher.
            if(!this.dispatcherRegistry.HasDispatcher(context.CallData.Identifier))
            {
                // Return the same state.
                return new SystemContractExecutionResult(initialState);
            }

            IDispatcher dispatcher = this.dispatcherRegistry.GetDispatcher(context.CallData.Identifier);

            // Invoke the contract.
            Result executionResult = dispatcher.Dispatch(context);

            if(executionResult.IsFailure)
            {
                // Return the same state.
                return new SystemContractExecutionResult(initialState);
            }

            // Return new state.
            return new SystemContractExecutionResult(state);
        }
    }
}
