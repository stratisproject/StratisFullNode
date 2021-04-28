using System;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Dynamically dispatches a method call to a first-class CLR type.
    /// </summary>
    public class SystemContractVm
    {
        public VmExecutionResult ExecuteMethod(string methodName, string typeName)
        {
            return null;
        }
    }
}
