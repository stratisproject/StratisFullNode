using System;
using System.Linq;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts.Compatibility
{
    /// <summary>
    /// Wrapper around the system contract runner for compatibility with existing SC execution model.
    /// </summary>
    public class SystemContractExecutor : IContractExecutor
    {
        private readonly ISystemContractRunner runner;
        private readonly IStateRepositoryRoot stateRepository;
        private readonly IWhitelistedHashChecker whitelistedHashChecker;
        private readonly ICallDataSerializer callDataSerializer;
        private ILogger logger;

        public SystemContractExecutor(ILoggerFactory loggerFactory, ISystemContractRunner runner, ICallDataSerializer callDataSerializer, IWhitelistedHashChecker whitelistedHashChecker, IStateRepositoryRoot stateRepository)
        {
            this.logger = loggerFactory.CreateLogger(typeof(SystemContractExecutor).FullName);
            this.runner = runner;
            this.stateRepository = stateRepository;
            this.whitelistedHashChecker = whitelistedHashChecker;
            this.callDataSerializer = callDataSerializer;
        }

        public IContractExecutionResult Execute(IContractTransactionContext transactionContext)
        {
            Result<ContractTxData> callDataDeserializationResult = this.callDataSerializer.Deserialize(transactionContext.Data);
            ContractTxData callData = callDataDeserializationResult.Value;

            var initialStateRoot = this.stateRepository.Root.ToArray(); // Use ToArray to make a copy

            var systemContractCall = new SystemContractCall(new Identifier(callData.ContractAddress), callData.MethodName, callData.MethodParameters, callData.VmVersion);

            // TODO currently need to call this with the padded identifier because the identifier is a uint160 while the whitelist is uint256
            if (!this.whitelistedHashChecker.CheckHashWhitelisted(systemContractCall.Identifier.Padded().ToBytes()))
            {
                this.logger.LogDebug("Contract is not whitelisted '{0}'.", systemContractCall.Identifier);

                return new SystemContractExecutionResult(callData.ContractAddress, null);
            }

            var context = new SystemContractTransactionContext(this.stateRepository, transactionContext.Transaction, systemContractCall);
            ISystemContractRunnerResult result = this.runner.Execute(context);

            // Only update if there was a change.
            if (!result.NewState.Root.SequenceEqual(initialStateRoot))
            {
                this.stateRepository.SyncToRoot(result.NewState.Root);
            }

            return new SystemContractExecutionResult(callData.ContractAddress, result.Result);
        }
    }
}
