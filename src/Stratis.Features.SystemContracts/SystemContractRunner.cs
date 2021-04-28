using CSharpFunctionalExtensions;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface ISystemContractRunner
    {
        ISystemContractExecutionResult Execute(ISystemContractTransactionContext context);
    }

    public class SystemContractRunner : ISystemContractRunner
    {
        private readonly IDispatcherRegistry dispatcherRegistry;

        public SystemContractRunner(IDispatcherRegistry dispatchers)
        {
            this.dispatcherRegistry = dispatchers;
        }

        public ISystemContractExecutionResult Execute(ISystemContractTransactionContext context)
        {
            IStateRepositoryRoot state = context.State;

            // Create a new copy of the initial state that we can return if we need to ignore the changes made.
            var initialRoot = state.Root;

            // Find the dispatcher.
            if (!this.dispatcherRegistry.HasDispatcher(context.CallData.Identifier))
            {
                // Return the same state.
                return new SystemContractExecutionResult(state);
            }

            IDispatcher dispatcher = this.dispatcherRegistry.GetDispatcher(context.CallData.Identifier);

            // Invoke the contract.
            Result executionResult = dispatcher.Dispatch(context);

            if (executionResult.IsFailure)
            {
                // Return to the root state.
                state.SyncToRoot(initialRoot);

                return new SystemContractExecutionResult(state);
            }

            // Return new state.
            return new SystemContractExecutionResult(state);
        }
    }
}
