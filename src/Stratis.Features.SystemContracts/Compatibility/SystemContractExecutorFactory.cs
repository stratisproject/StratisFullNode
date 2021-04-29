using System.Text;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts.Compatibility
{
    /// <summary>
    /// Wrapper around the system contract runner for compatibility.
    /// </summary>
    public class SystemContractExecutorFactory : IContractExecutorFactory
    {
        private readonly ISystemContractRunner runner;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly IWhitelistedHashChecker whitelistedHashChecker;

        public SystemContractExecutorFactory(ISystemContractRunner runner, ICallDataSerializer callDataSerializer, IWhitelistedHashChecker whitelistedHashChecker)
        {
            this.runner = runner;
            this.callDataSerializer = callDataSerializer;
            this.whitelistedHashChecker = whitelistedHashChecker;
        }

        public IContractExecutor CreateExecutor(IStateRepositoryRoot stateRepository, IContractTransactionContext transactionContext)
        {
            return new SystemContractExecutor(this.runner, this.callDataSerializer, this.whitelistedHashChecker, stateRepository);
        }
    }
}
