using CSharpFunctionalExtensions;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public class SystemContractRunner
    {
        public SystemContractRunner(IDispatcherRegistry dispatchers)
        {
            this.Dispatchers = dispatchers;
        }

        public IDispatcherRegistry Dispatchers { get; }

        public ISystemContractExecutionResult Execute(ISystemContractTransactionContext context)
        {
            // Create a new copy of the initial state that we can return if we need to ignore the changes made.
            IStateRepository initialState = context.State.StartTracking();

            IStateRepositoryRoot state = context.State;

            // Invoke the dispatcher
            if(!this.Dispatchers.HasDispatcher(context.CallData.Identifier))
            {
                // Return the same state.
                return new SystemContractExecutionResult(initialState);
            }

            IDispatcher dispatcher = this.Dispatchers.GetDispatcher(context.CallData.Identifier);

            Result executionResult = dispatcher.Dispatch(context);

            if(executionResult.IsFailure)
            {
                // Return the same state.
                return new SystemContractExecutionResult(initialState);
            }

            // Return new state
            return new SystemContractExecutionResult(state);
        }
    }
}
