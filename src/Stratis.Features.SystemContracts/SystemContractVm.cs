using System;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Dynamically dispatches a method call to a first-class CLR type.
    /// </summary>
    public class SystemContractVm :IVirtualMachine
    {
        public VmExecutionResult Create(IStateRepository repository, ISmartContractState contractState, ExecutionContext executionContext, byte[] contractCode, object[] parameters, string typeName = null)
        {
            throw new NotImplementedException();
        }

        public VmExecutionResult ExecuteMethod(ISmartContractState contractState, ExecutionContext executionContext, MethodCall methodCall, byte[] contractCode, string typeName)
        {
            throw new NotImplementedException();
        }
    }
}
