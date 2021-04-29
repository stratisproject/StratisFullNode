using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.CLR.Local;
using Stratis.SmartContracts.Core.Receipts;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.RuntimeObserver;

namespace Stratis.Features.SystemContracts.Compatibility
{
    public class SystemContractLocalExecutor : ILocalExecutor
    {
        private readonly IStateRepositoryRoot state;
        private readonly ISystemContractRunner runner;
        private readonly ChainIndexer chainIndexer;

        public SystemContractLocalExecutor(IStateRepositoryRoot state, ISystemContractRunner runner, ChainIndexer chainIndexer)
        {
            this.state = state;
            this.runner = runner;
            this.chainIndexer = chainIndexer;
        }

        /// <summary>
        /// Execute a query on the system contract at the given block height.
        /// </summary>
        /// <param name="blockHeight"></param>
        /// <param name="sender"></param>
        /// <param name="txOutValue"></param>
        /// <param name="callData"></param>
        /// <returns></returns>
        public ILocalExecutionResult Execute(ulong blockHeight, uint160 sender, Money txOutValue, ContractTxData callData)
        {
            ChainedHeader chainedHeader = this.chainIndexer.GetHeader(blockHeight);

            var scHeader = chainedHeader?.Header as ISmartContractBlockHeader;

            var isScHeader = scHeader != null && scHeader.HasSmartContractFields;
            
            if (!isScHeader)
            {
                return new LocalExecutionResult
                {
                    GasConsumed = (Gas)0,
                    InternalTransfers = null,
                    ErrorMessage = null,
                    Logs = new List<Log>(),
                    Return = null,
                    Revert = false
                };
            }

            uint256 hashStateRoot = scHeader.HashStateRoot;

            IStateRepositoryRoot stateSnapshot = this.state.GetSnapshotTo(hashStateRoot.ToBytes());

            var systemContractCall = new SystemContractCall(callData.ContractAddress, callData.MethodName, callData.MethodParameters, callData.VmVersion);

            var context = new SystemContractTransactionContext(stateSnapshot, null, systemContractCall);

            ISystemContractRunnerResult result = this.runner.Execute(context);

            return new LocalExecutionResult
            {
                GasConsumed = (Gas)0,
                InternalTransfers = null,
                ErrorMessage = null,
                Logs = new List<Log>(),
                Return = result.Result,
                Revert = false
            };
        }
    }
}
