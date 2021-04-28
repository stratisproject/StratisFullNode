using System;
using Stratis.Bitcoin.Features.SmartContracts.Interfaces;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.ResultProcessors;
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
        private readonly IContractRefundProcessor refundProcessor;
        private readonly IContractTransferProcessor transferProcessor;
        private readonly IStateFactory stateFactory;
        private readonly IStateProcessor stateProcessor;

        public SystemContractExecutorFactory(ICallDataSerializer callDataSerializer, 
            IContractRefundProcessor refundProcessor,
            IContractTransferProcessor transferProcessor,
            IStateFactory stateFactory,
            IStateProcessor stateProcessor)
        {
            this.callDataSerializer = callDataSerializer;
            this.refundProcessor = refundProcessor;
            this.transferProcessor = transferProcessor;
            this.stateFactory = stateFactory;
            this.stateProcessor = stateProcessor;
        }

        public IContractExecutor CreateExecutor(IStateRepositoryRoot stateRepository, IContractTransactionContext transactionContext)
        {
            return new SystemContractExecutor(this.callDataSerializer, stateRepository, this.refundProcessor, this.transferProcessor, this.stateFactory, this.stateProcessor);
        }
    }
}
