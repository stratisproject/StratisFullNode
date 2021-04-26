using System;
using System.Collections.Generic;
using CSharpFunctionalExtensions;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Executor for system contracts. When given a transaction context for a system contract invocation,
    /// will find the relevant system contract, check its whitelisting status and dispatch the invocation for the requested method.
    /// </summary>
    public class SystemContractExecutor
    {
        private readonly IStateRepositoryRoot stateRoot;
        private readonly IWhitelistedHashChecker whitelistedHashChecker;
        private readonly ICallDataSerializer callDataSerializer;

        public SystemContractExecutor(ICallDataSerializer serializer, IStateRepositoryRoot stateRoot, IWhitelistedHashChecker whitelistedHashChecker)
        {
            this.stateRoot = stateRoot;
            this.whitelistedHashChecker = whitelistedHashChecker;
            this.callDataSerializer = serializer;
        }

        public ISystemContractExecutionResult Execute(IContractTransactionContext transactionContext)
        {
            // Deserialization can't fail because this has already been through SmartContractFormatRule.
            Result<ContractTxData> callDataDeserializationResult = this.callDataSerializer.Deserialize(transactionContext.Data);
            ContractTxData callData = callDataDeserializationResult.Value;

            // TODO assert this in a consensus rule before we ever get here.
            if (callData.IsCreateContract)
                throw new InvalidOperationException("Contract creation transactions are not permitted");

            // TODO get the contract type information

            // If it's not whitelisted then nothing changes.
            if(!this.whitelistedHashChecker.CheckHashWhitelisted(null))
            {
                return new SystemContractExecutionResult(this.stateRoot);
            }

            // TODO
            return null;
        }
    }
}
