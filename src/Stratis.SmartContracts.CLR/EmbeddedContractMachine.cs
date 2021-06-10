using System;
using System.Linq;
using System.Reflection;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.SmartContracts.CLR.Caching;
using Stratis.SmartContracts.CLR.Compilation;
using Stratis.SmartContracts.CLR.Loader;
using Stratis.SmartContracts.CLR.Validation;

namespace Stratis.SmartContracts.CLR
{
    public class EmbeddedContractMachine : ReflectionVirtualMachine
    {
        private readonly IServiceProvider serviceProvider;
        private readonly IEmbeddedContractContainer embeddedContractContainer;
        private readonly ILogger logger;

        public EmbeddedContractMachine(ISmartContractValidator validator,
           ILoggerFactory loggerFactory,
           ILoader assemblyLoader,
           IContractModuleDefinitionReader moduleDefinitionReader,
           IContractAssemblyCache assemblyCache,
           IServiceProvider serviceProvider,
           IEmbeddedContractContainer embeddedContractContainer) : base(validator, loggerFactory, assemblyLoader, moduleDefinitionReader, assemblyCache)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType());
            this.serviceProvider = serviceProvider;
            this.embeddedContractContainer = embeddedContractContainer;
        }

        public override VmExecutionResult ExecuteMethod(ISmartContractState contractState, ExecutionContext executionContext,
            MethodCall methodCall, byte[] contractCode, string typeName)
        {
            uint160 address = contractState.Message.ContractAddress.ToUint160();

            if (!EmbeddedContractIdentifier.IsEmbedded(address))
            {
                return base.ExecuteMethod(contractState, executionContext, methodCall, contractCode, typeName);
            }

            if (!this.embeddedContractContainer.TryGetContractTypeAndVersion(address, out typeName, out uint version))
            {
                this.logger.LogDebug("CREATE_CONTRACT_INSTANTIATION_FAILED");
                return VmExecutionResult.Fail(VmExecutionErrorKind.InvocationFailed, "The embedded contract is not registered.");
            }

            // TODO: Verify that the contract is white-listed by IWhitelistedHashChecker. The particular version may not have been BIP activated yet.             

            Type type = Type.GetType(typeName);
            IContract contract = Contract.CreateUninitialized(type, contractState, address);

            // Invoke the constructor of the provided contract code
            Result<object[]> result = GetEmbeddedConstructorParameters(type, version);
            if (result.IsFailure)
            {
                this.logger.LogDebug("CREATE_CONTRACT_INSTANTIATION_FAILED");
                return VmExecutionResult.Fail(VmExecutionErrorKind.InvocationFailed, result.Error);
            }

            IContractInvocationResult invocationResult = contract.InvokeConstructor(result.Value);

            if (!invocationResult.IsSuccess)
            {
                this.logger.LogDebug("CREATE_CONTRACT_INSTANTIATION_FAILED");
                return GetInvocationVmErrorResult(invocationResult);
            }

            this.logger.LogDebug("CREATE_CONTRACT_INSTANTIATION_SUCCEEDED");

            this.LogExecutionContext(contract.State.Block, contract.State.Message, contract.Address);

            invocationResult = contract.Invoke(methodCall);

            if (!invocationResult.IsSuccess)
            {
                this.logger.LogTrace("(-)[CALLCONTRACT_INSTANTIATION_FAILED]");
                return GetInvocationVmErrorResult(invocationResult);
            }

            this.logger.LogDebug("CALL_CONTRACT_INSTANTIATION_SUCCEEDED");
            return VmExecutionResult.Ok(invocationResult.Return, typeName);
        }

        private Result<object[]> GetEmbeddedConstructorParameters(Type type, uint version)
        {
            // If its an embedded contract then we feed the constructor anything it wants including other contracts.
            // Note: Only one (backwards-compatible) contructor is allowed.
            ConstructorInfo info = type.GetConstructors().Single();
            ParameterInfo[] parameterTypes = info.GetParameters().ToArray();
            if (parameterTypes[0].ParameterType != typeof(ISmartContractState))
            {
                const string typeNotFoundError = "Invalid constructor!";

                this.logger.LogDebug(typeNotFoundError);

                return Result.Fail<object[]>($"Constructor should contain a first argument of type {nameof(ISmartContractState)}.");
            }

            parameterTypes = parameterTypes.Skip(1).ToArray();
            var parameters = new object[parameterTypes.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameterTypes[i].ParameterType;
                parameters[i] = this.serviceProvider.GetService(parameterType);
            }

            return Result.Ok(parameters);
        }
    }
}
