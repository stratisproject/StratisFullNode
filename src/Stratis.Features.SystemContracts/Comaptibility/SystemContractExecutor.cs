using System;
using System.Linq;
using CSharpFunctionalExtensions;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts.Comaptibility
{
    /// <summary>
    /// Wrapper around the system contract runner for compatibility with existing SC execution model.
    /// </summary>
    public class SystemContractExecutor : IContractExecutor
    {
        private readonly ISystemContractRunner runner;
        private readonly IStateRepositoryRoot stateRepository;
        private readonly ICallDataSerializer callDataSerializer;

        public SystemContractExecutor(ISystemContractRunner runner, ICallDataSerializer callDataSerializer, IStateRepositoryRoot stateRepository)
        {
            this.runner = runner;
            this.stateRepository = stateRepository;
            this.callDataSerializer = callDataSerializer;
        }

        public IContractExecutionResult Execute(IContractTransactionContext transactionContext)
        {
            Result<ContractTxData> callDataDeserializationResult = this.callDataSerializer.Deserialize(transactionContext.Data);
            ContractTxData callData = callDataDeserializationResult.Value;

            var initialStateRoot = this.stateRepository.Root.ToArray(); // Use ToArray to make a copy

            var systemContractCall = new SystemContractCall(callData.ContractAddress, callData.MethodName, callData.MethodParameters, callData.VmVersion);
            var context = new SystemContractTransactionContext(this.stateRepository, transactionContext.Transaction, systemContractCall);
            ISystemContractRunnerResult result = this.runner.Execute(context);

            // Only update if there was a change.
            if (!result.NewState.Root.SequenceEqual(initialStateRoot))
            {
                this.stateRepository.SyncToRoot(result.NewState.Root);
            }

            return new SystemContractExecutionResult(callData.ContractAddress);
        }
    }
}
