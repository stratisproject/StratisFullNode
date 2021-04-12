using Stratis.SmartContracts.Core.State;

namespace Stratis.SmartContracts.CLR
{
    public interface IVirtualMachine
    {
        VmExecutionResult Create(IStateRepository repository,
            ISmartContractState contractState,
            ExecutionContext executionContext,
            byte[] contractCode,
            object[] parameters,
            string typeName = null);

        VmExecutionResult ExecuteMethod(ISmartContractState contractState,
            ExecutionContext executionContext,
            MethodCall methodCall,
            byte[] contractCode,
            string typeName);
    }
}
