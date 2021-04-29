using System.Text;
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

        public SystemContractExecutorFactory(ISystemContractRunner runner, ICallDataSerializer callDataSerializer)
        {
            this.runner = runner;
            this.callDataSerializer = callDataSerializer;
        }

        public IContractExecutor CreateExecutor(IStateRepositoryRoot stateRepository, IContractTransactionContext transactionContext)
        {
            return new SystemContractExecutor(this.runner, this.callDataSerializer, stateRepository);
        }
    }
}
