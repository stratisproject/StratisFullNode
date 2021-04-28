using System;
using System.Collections.Generic;
using CSharpFunctionalExtensions;
using NBitcoin;
using Stratis.SmartContracts;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.ContractLogging;
using Stratis.SmartContracts.CLR.ResultProcessors;
using Stratis.SmartContracts.CLR.Serialization;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.State.AccountAbstractionLayer;
using Stratis.SmartContracts.RuntimeObserver;
using Block = Stratis.SmartContracts.Block;

namespace Stratis.Features.SystemContracts
{
    public class SystemContractState : IState
    {
        private readonly List<TransferInfo> internalTransfers;
        private IState child;

        private SystemContractState(State state)
        {
            this.ContractState = state.ContractState.StartTracking();

            // We create a new list but use references to the original transfers.
            this.internalTransfers = new List<TransferInfo>(state.InternalTransfers);

            // Create a new balance state based off the old one but with the repository and internal transfers list reference
            this.BalanceState = new BalanceState(this.ContractState, this.internalTransfers, state.BalanceState.InitialTransfer);
            this.Block = state.Block;
            this.TransactionHash = state.TransactionHash;
        }

        public SystemContractState()
        {

        }
        public IBlock Block { get; }

        public uint256 TransactionHash { get; }

        public BalanceState BalanceState { get; }
        }

        public IStateRepository ContractState { get; private set; }

        public IReadOnlyList<TransferInfo> InternalTransfers { get; }

        public IContractLogHolder LogHolder => null;

        public NonceGenerator NonceGenerator => null;

        public void AddInitialTransfer(TransferInfo initialTransfer)
        {
            throw new NotImplementedException();
        }

        public void AddInternalTransfer(TransferInfo transferInfo)
        {
            throw new NotImplementedException();
        }

        public ISmartContractState CreateSmartContractState(IState state, IGasMeter gasMeter, uint160 address, BaseMessage message, IStateRepository repository)
        {
            throw new NotImplementedException();
        }

        public uint160 GenerateAddress(IAddressGenerator addressGenerator)
        {
            throw new NotImplementedException();
        }

        public ulong GetBalance(uint160 address)
        {
            throw new NotImplementedException();
        }

        public IList<Log> GetLogs(IContractPrimitiveSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public IState Snapshot()
        {
            throw new NotImplementedException();
        }

        public void TransitionTo(IState state)
        {
            throw new NotImplementedException();
        }
    }
    /// <summary>
    /// Executor for system contracts. When given a transaction context for a system contract invocation,
    /// will find the relevant system contract, check its whitelisting status and dispatch the invocation for the requested method.
    /// </summary>
    public class SystemContractExecutor : IContractExecutor
    {
        private readonly IStateRepository stateRoot;
        private readonly IContractRefundProcessor refundProcessor;
        private readonly IContractTransferProcessor transferProcessor;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly IStateFactory stateFactory;
        private readonly IStateProcessor stateProcessor;

        public SystemContractExecutor(
            ICallDataSerializer callDataSerializer,
            IStateRepository stateRoot,
            IContractRefundProcessor refundProcessor,
            IContractTransferProcessor transferProcessor,
            IStateFactory stateFactory,
            IStateProcessor stateProcessor)
        {
            this.stateRoot = stateRoot;
            this.refundProcessor = refundProcessor;
            this.transferProcessor = transferProcessor;
            this.callDataSerializer = callDataSerializer;
            this.stateFactory = stateFactory;
            this.stateProcessor = stateProcessor;
        }

        public IContractExecutionResult Execute(IContractTransactionContext transactionContext)
        {
            // Deserialization can't fail because this has already been through SmartContractFormatRule.
            Result<ContractTxData> callDataDeserializationResult = this.callDataSerializer.Deserialize(transactionContext.Data);
            ContractTxData callData = callDataDeserializationResult.Value;

            // TODO assert this in a consensus rule before we ever get here.
            if (callData.IsCreateContract)
                throw new InvalidOperationException("Contract creation transactions are not permitted");

            bool creation = callData.IsCreateContract;

            var block = new Block(
                transactionContext.BlockHeight,
                transactionContext.CoinbaseAddress.ToAddress()
            );

            IState state = this.stateFactory.Create(
                this.stateRoot,
                block,
                transactionContext.TxOutValue,
                transactionContext.TransactionHash);

            StateTransitionResult result;
            IState newState = state.Snapshot();

            var message = new ExternalCallMessage(
                    callData.ContractAddress,
                    transactionContext.Sender,
                    transactionContext.TxOutValue,
                    callData.GasLimit,
                    new MethodCall(callData.MethodName, callData.MethodParameters)
            );

            result = this.stateProcessor.Apply(newState, message);

            bool revert = !result.IsSuccess;

            Transaction internalTransaction = this.transferProcessor.Process(
                newState.ContractState,
                result.Success?.ContractAddress,
                transactionContext,
                newState.InternalTransfers,
                revert);

            if (result.IsSuccess)
                state.TransitionTo(newState);

            bool outOfGas = false;

            // TODO v1 - refund everything except fees
            (Money fee, TxOut refundTxOut) = this.refundProcessor.Process(
                callData,
                transactionContext.MempoolFee,
                transactionContext.Sender,
                result.GasConsumed,
                outOfGas);

            var executionResult = new SmartContractExecutionResult
            {
                To = !callData.IsCreateContract ? callData.ContractAddress : null,
                NewContractAddress = !revert && creation ? result.Success?.ContractAddress : null,
                ErrorMessage = result.Error?.GetErrorMessage(),
                Revert = revert,
                GasConsumed = result.GasConsumed,
                Return = result.Success?.ExecutionResult,
                InternalTransaction = internalTransaction,
                Fee = fee,
                Refund = refundTxOut,
                Logs = new List<Log>()
            };

            return executionResult;
        }
    }
}
