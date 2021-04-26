using System;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Factory for creating an executor that runs system contracts that are first-class types.
    /// </summary>
    public class SystemContractExecutorFactory : IContractExecutorFactory
    {
        private readonly ICallDataSerializer callDataSerializer;
        private readonly IWhitelistedHashChecker whitelistedHashChecker;

        public SystemContractExecutorFactory(ICallDataSerializer callDataSerializer, IWhitelistedHashChecker whitelistedHashChecker)
        {
            this.callDataSerializer = callDataSerializer;
            this.whitelistedHashChecker = whitelistedHashChecker;
        }

        public IContractExecutor CreateExecutor(IStateRepositoryRoot stateRepository, IContractTransactionContext transactionContext)
        {
            return new SystemContractExecutor(this.callDataSerializer, stateRepository, this.whitelistedHashChecker);
        }
    }
}
