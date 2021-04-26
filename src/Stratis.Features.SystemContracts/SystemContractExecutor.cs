using System;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.ResultProcessors;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Executor for system contracts. When given a transaction context for a system contract invocation,
    /// will find the relevant system contract, check its whitelisting status and invoke the requested method.
    /// </summary>
    public class SystemContractExecutor : IContractExecutor
    {
        private readonly IStateRepository stateRoot;
        private readonly IContractRefundProcessor refundProcessor;
        private readonly IContractTransferProcessor transferProcessor;
        private readonly ICallDataSerializer serializer;
        private readonly IStateFactory stateFactory;
        private readonly IStateProcessor stateProcessor;
        private readonly IContractPrimitiveSerializer contractPrimitiveSerializer;

        public SystemContractExecutor(
            ICallDataSerializer serializer,
            IStateRepository stateRoot,
            IContractRefundProcessor refundProcessor,
            IContractTransferProcessor transferProcessor,
            IStateFactory stateFactory,
            IStateProcessor stateProcessor,
            IContractPrimitiveSerializer contractPrimitiveSerializer)
        {
            this.stateRoot = stateRoot;
            this.refundProcessor = refundProcessor;
            this.transferProcessor = transferProcessor;
            this.serializer = serializer;
            this.stateFactory = stateFactory;
            this.stateProcessor = stateProcessor;
            this.contractPrimitiveSerializer = contractPrimitiveSerializer;
        }

        public IContractTransferProcessor TransferProcessor { get; }

        public IContractExecutionResult Execute(IContractTransactionContext transactionContext)
        {
            throw new NotImplementedException();
        }
    }
}
