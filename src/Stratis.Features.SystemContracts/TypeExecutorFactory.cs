using System;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Factory for creating an executor that runs system contracts that are first-class types.
    /// </summary>
    public class TypeExecutorFactory : IContractExecutorFactory
    {
        public IContractExecutor CreateExecutor(IStateRepositoryRoot stateRepository, IContractTransactionContext transactionContext)
        {
            throw new NotImplementedException();
        }
    }
}
