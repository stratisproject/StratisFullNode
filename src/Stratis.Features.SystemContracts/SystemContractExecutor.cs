using System;
using System.Collections.Generic;
using CSharpFunctionalExtensions;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Receipts;
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

        public SystemContractExecutor(
            ICallDataSerializer serializer,
            IStateRepository stateRoot)
        {
            this.stateRoot = stateRoot;
            this.serializer = serializer;
        }

        public IContractExecutionResult Execute(IContractTransactionContext transactionContext)
        {            
            // Deserialization can't fail because this has already been through SmartContractFormatRule.
            Result<ContractTxData> callDataDeserializationResult = this.serializer.Deserialize(transactionContext.Data);
            ContractTxData callData = callDataDeserializationResult.Value;

            // TODO assert this in a consensus rule before we ever get here.
            if (callData.IsCreateContract)
                throw new InvalidOperationException("Contract creation transactions are not permitted");


            var executionResult = new SmartContractExecutionResult
            {
                To = !callData.IsCreateContract ? callData.ContractAddress : null,
                NewContractAddress = null,
                ErrorMessage = null,
                Revert = false,
                Return = null, // TODO
                InternalTransaction = null,
                Fee = 0,
                Refund = null,
                Logs = new List<Log>()
            };

            return executionResult;
        }
    }
}
