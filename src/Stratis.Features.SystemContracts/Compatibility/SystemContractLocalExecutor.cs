using System;
using NBitcoin;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Local;

namespace Stratis.Features.SystemContracts.Compatibility
{
    public class SystemContractLocalExecutor : ILocalExecutor
    {
        public ILocalExecutionResult Execute(ulong blockHeight, uint160 sender, Money txOutValue, ContractTxData txData)
        {
            throw new NotImplementedException();
        }
    }
}
