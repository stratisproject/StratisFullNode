using CSharpFunctionalExtensions;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    public interface ISystemContractRunner
    {
        ISystemContractRunnerResult Execute(ISystemContractTransactionContext context);
    }

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
            Result executionResult = dispatcher.Dispatch(context);

            if (executionResult.IsFailure)
            {
                // Return to the root state.
                state.SyncToRoot(initialRoot);

                return new SystemContractRunnerResult(state);
            }

            // Return new state.
            return new SystemContractRunnerResult(state);
        }
    }
}
