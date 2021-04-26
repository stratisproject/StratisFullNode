using System;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Executor for system contracts. When given a transaction context for a system contract invocation,
    /// will find the relevant system contract, check its whitelisting status and invoke the requested method.
    /// </summary>
    public class SystemContractExecutor : IContractExecutor
    {
        private readonly IStateRepository stateRoot;
        private readonly ICallDataSerializer serializer;
        private readonly IStateProcessor stateProcessor;

        public SystemContractExecutor(
            ICallDataSerializer serializer,
            IStateRepository stateRoot,
            IStateProcessor stateProcessor)
        {
            this.stateRoot = stateRoot;
            this.serializer = serializer;
            this.stateProcessor = stateProcessor;
        }

        public IContractExecutionResult Execute(IContractTransactionContext transactionContext)
        {
            throw new NotImplementedException();
        }
    }
}
