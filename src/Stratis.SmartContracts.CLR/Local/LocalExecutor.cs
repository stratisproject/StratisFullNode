using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.RuntimeObserver;

namespace Stratis.SmartContracts.CLR.Local
{
    /// <summary>
    /// Executes a contract with the specified parameters without making changes to the state database or chain.
    /// </summary>
    public class LocalExecutor : ILocalExecutor
    {
        private readonly ILogger logger;
        private readonly IStateRepository stateRoot;
        private readonly ICallDataSerializer serializer;
        private readonly IStateFactory stateFactory;
        private readonly IStateProcessor stateProcessor;
        private readonly IContractPrimitiveSerializer contractPrimitiveSerializer;
        private readonly ChainIndexer chainIndexer;

        public LocalExecutor(ILoggerFactory loggerFactory,
            ICallDataSerializer serializer,
            IStateRepositoryRoot stateRoot,
            IStateFactory stateFactory,
            IStateProcessor stateProcessor,
            IContractPrimitiveSerializer contractPrimitiveSerializer,
            ChainIndexer chainIndexer)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType());
            this.stateRoot = stateRoot;
            this.serializer = serializer;
            this.stateFactory = stateFactory;
            this.stateProcessor = stateProcessor;
            this.contractPrimitiveSerializer = contractPrimitiveSerializer;
            this.chainIndexer = chainIndexer;
        }

        public ILocalExecutionResult Execute(ulong blockHeight, uint160 sender, Money txOutValue, ContractTxData callData)
        {
            bool creation = callData.IsCreateContract;

            var block = new Block(
                blockHeight,
                Address.Zero
            );

            ChainedHeader chainedHeader = this.chainIndexer.GetHeader((int)blockHeight);

            var scHeader = chainedHeader?.Header as ISmartContractBlockHeader;

            uint256 hashStateRoot = scHeader?.HashStateRoot ?? new uint256("21B463E3B52F6201C0AD6C991BE0485B6EF8C092E64583FFA655CC1B171FE856"); // StateRootEmptyTrie

            IStateRepositoryRoot stateAtHeight = this.stateRoot.GetSnapshotTo(hashStateRoot.ToBytes());

            IState state = this.stateFactory.Create(
                stateAtHeight.StartTracking(),
                block,
                txOutValue,
                new uint256());

            StateTransitionResult result;
            IState newState = state.Snapshot();

            if (creation)
            {
                var message = new ExternalCreateMessage(
                    sender,
                    txOutValue,
                    callData.GasLimit,
                    callData.ContractExecutionCode,
                    callData.MethodParameters
                );

                result = this.stateProcessor.Apply(newState, message);
            }
            else
            {
                var message = new ExternalCallMessage(
                        callData.ContractAddress,
                        sender,
                        txOutValue,
                        callData.GasLimit,
                        new MethodCall(callData.MethodName, callData.MethodParameters)
                );

                result = this.stateProcessor.Apply(newState, message);
            }
            
            var executionResult = new LocalExecutionResult
            {
                ErrorMessage = result.Error?.GetErrorMessage(),
                Revert = result.IsFailure,
                GasConsumed = result.GasConsumed,
                Return = result.Success?.ExecutionResult,
                InternalTransfers = state.InternalTransfers.ToList(),
                Logs = state.GetLogs(this.contractPrimitiveSerializer)
            };

            return executionResult;
        }
    }
}
