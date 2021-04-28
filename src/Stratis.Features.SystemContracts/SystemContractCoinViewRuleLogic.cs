using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus;
using Stratis.Bitcoin.Features.Consensus.CoinViews;
using Stratis.Bitcoin.Features.Consensus.Interfaces;
using Stratis.Bitcoin.Features.SmartContracts;
using Stratis.Bitcoin.Features.SmartContracts.PoW;
using Stratis.SmartContracts.CLR;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.State;
using Stratis.SmartContracts.Core.Util;

namespace Stratis.Features.SystemContracts
{
    /// <summary>
    /// Coin view rule logic for system contracts.
    /// 
    /// Checks whether a contract execution is required and hands off to the contract executor if so.
    /// </summary>
    public class SystemContractCoinViewRuleLogic : ISmartContractCoinViewRuleLogic
    {
        private readonly ICoinView coinView;
        private readonly IStateRepositoryRoot stateRepositoryRoot;
        private readonly ISenderRetriever senderRetriever;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly ISystemContractRunner systemContractRunner;
        private readonly List<Transaction> blockTxsProcessed;
        private ILogger<SystemContractCoinViewRuleLogic> logger;
        private IStateRepositoryRoot mutableStateRepository;

        public SystemContractCoinViewRuleLogic(
            ILoggerFactory loggerFactory, 
            ICoinView coinView, 
            IStateRepositoryRoot stateRepositoryRoot, 
            ISenderRetriever senderRetriever,
            ICallDataSerializer callDataSerializer,
            ISystemContractRunner systemContractRunner)
        {
            this.logger = loggerFactory.CreateLogger<SystemContractCoinViewRuleLogic>();
            this.coinView = coinView;
            this.stateRepositoryRoot = stateRepositoryRoot;
            this.senderRetriever = senderRetriever;
            this.callDataSerializer = callDataSerializer;
            this.systemContractRunner = systemContractRunner;
            this.blockTxsProcessed = new List<Transaction>();
        }

        /// <summary>
        /// Wraps the base coin view rule with a contract execution.
        /// </summary>
        /// <param name="baseRunAsync"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task RunAsync(Func<RuleContext, Task> baseRunAsync, RuleContext context)
        {
            // Reset the fields that are hackily being used to pass values between methods.
            this.blockTxsProcessed.Clear();
            this.mutableStateRepository = null;

            Block block = context.ValidationContext.BlockToValidate;
            this.logger.LogDebug("Block to validate '{0}'", block.GetHash());

            // Get a IStateRepositoryRoot we can alter without affecting the injected one which is used elsewhere.
            BlockHeader prevHeader = context.ValidationContext.ChainedHeaderToValidate.Previous.Header;
            uint256 blockRoot;
            if (!(prevHeader is PosBlockHeader posHeader) || posHeader.HasSmartContractFields)
                blockRoot = ((ISmartContractBlockHeader)prevHeader).HashStateRoot;
            else
                blockRoot = SmartContractBlockDefinition.StateRootEmptyTrie;

            this.logger.LogDebug("Block hash state root '{0}'.", blockRoot);

            this.mutableStateRepository = this.stateRepositoryRoot.GetSnapshotTo(blockRoot.ToBytes());

            // CONTRACT EXECUTION
            // Call chain base->CheckInput->UpdateCoinView->ExecuteContract.
            // Once this is completed we expect this.mutableStateRepositoryRoot to be updated
            await baseRunAsync(context);

            var blockHeader = (ISmartContractBlockHeader)block.Header;

            // CONSENSUS - Check that the execution resulted in an identical hashstateroot to the block header
            var mutableStateRepositoryRoot = new uint256(this.mutableStateRepository.Root);
            uint256 blockHeaderHashStateRoot = blockHeader.HashStateRoot;
            this.logger.LogDebug("Compare state roots '{0}' and '{1}'", mutableStateRepositoryRoot, blockHeaderHashStateRoot);
            if (mutableStateRepositoryRoot != blockHeaderHashStateRoot)
                SmartContractConsensusErrors.UnequalStateRoots.Throw();

            // Push to underlying database
            this.mutableStateRepository.Commit();

            // Update the globally injected state so all services receive the updates.
            this.stateRepositoryRoot.SyncToRoot(this.mutableStateRepository.Root);
        }

        /// <summary>
        /// Invoked by the base <see cref="CoinViewRule"/> inside CheckInput. Checks that we have a smart contract call.
        /// </summary>
        /// <param name="baseCheckInput"></param>
        /// <param name="tx"></param>
        /// <param name="inputIndexCopy"></param>
        /// <param name="txout"></param>
        /// <param name="txData"></param>
        /// <param name="input"></param>
        /// <param name="flags"></param>
        /// <returns></returns>
        public bool CheckInput(Func<Transaction, int, TxOut, PrecomputedTransactionData, TxIn, DeploymentFlags, bool> baseCheckInput, Transaction tx, int inputIndexCopy, TxOut txout, PrecomputedTransactionData txData, TxIn input, DeploymentFlags flags)
        {
            if (txout.ScriptPubKey.IsSmartContractCall())
            {
                return input.ScriptSig.IsSmartContractSpend();
            }

            return baseCheckInput(tx, inputIndexCopy, txout, txData, input, flags);
        }

        /// <summary>
        /// Invoked by the base <see cref="CoinViewRule"/> inside CheckInput
        /// </summary>
        /// <param name="baseUpdateUTXOSet"></param>
        /// <param name="context"></param>
        /// <param name="transaction"></param>
        public void UpdateCoinView(Action<RuleContext, Transaction> baseUpdateUTXOSet, RuleContext context, Transaction transaction)
        {
            this.blockTxsProcessed.Add(transaction);

            TxOut smartContractTxOut = transaction.Outputs.FirstOrDefault(txOut => SmartContractScript.IsSmartContractCall(txOut.ScriptPubKey));
            
            if (smartContractTxOut == null)
            {
                // Someone submitted a standard transaction - no smart contract call.
                baseUpdateUTXOSet(context, transaction);
                return;
            }

            this.mutableStateRepository = this.ExecuteContractTransaction(context, transaction);

            // Currently anything to do with transferring funds is not allowed, but this would be added here in the future.
        }

        private IStateRepositoryRoot ExecuteContractTransaction(RuleContext context, Transaction transaction)
        {
            // This should never be null because it's checked in a consensus rule.
            var serializedCallData = transaction.Outputs.FirstOrDefault(x => x.ScriptPubKey.IsSmartContractExec())?.ScriptPubKey.ToBytes();

            // This should always be successful because it's checked in a consensus rule.
            // TODO we can consider changing the call data serialization scheme to something that suits our needs better?
            ContractTxData callData = this.callDataSerializer.Deserialize(serializedCallData).Value;

            var systemContractCall = new SystemContractCall(callData.ContractAddress, callData.MethodName, callData.MethodParameters, callData.VmVersion);

            var systemContractContext = new SystemContractTransactionContext(this.mutableStateRepository, context.ValidationContext.BlockToValidate, transaction, systemContractCall);

            ISystemContractExecutionResult executionResult = this.systemContractRunner.Execute(systemContractContext);

            return executionResult.NewState;
        }
    }
}
