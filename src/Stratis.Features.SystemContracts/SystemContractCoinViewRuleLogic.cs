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
        private readonly List<Transaction> blockTxsProcessed;
        private ILogger<SystemContractCoinViewRuleLogic> logger;
        private IStateRepositoryRoot mutableStateRepository;

        public SystemContractCoinViewRuleLogic(ILoggerFactory loggerFactory, ICoinView coinView, IStateRepositoryRoot stateRepositoryRoot, ISenderRetriever senderRetriever)
        {
            this.logger = loggerFactory.CreateLogger<SystemContractCoinViewRuleLogic>();
            this.coinView = coinView;
            this.stateRepositoryRoot = stateRepositoryRoot;
            this.senderRetriever = senderRetriever;
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

            // Call chain base->CheckInput->UpdateCoinView->ExecuteContract.
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
        }


        /// <summary>
        /// Executes the smart contract part of a transaction and returns the state repository after execution.
        /// </summary>
        private IStateRepositoryRoot ExecuteContractTransaction(RuleContext context, Transaction transaction)
        {
            IContractTransactionContext txContext = this.GetSmartContractTransactionContext(context, transaction);

            // TODO execute
            //IContractExecutor executor = this.executorFactory.CreateExecutor(this.mutableStateRepository, txContext);
            //Result<ContractTxData> deserializedCallData = this.callDataSerializer.Deserialize(txContext.Data);

            //IContractExecutionResult result = executor.Execute(txContext);
            return null;
        }

        /// <summary>
        /// Retrieves the context object to be given to the contract executor.
        /// </summary>
        public IContractTransactionContext GetSmartContractTransactionContext(RuleContext context, Transaction transaction)
        {
            ulong blockHeight = Convert.ToUInt64(context.ValidationContext.ChainedHeaderToValidate.Height);

            GetSenderResult getSenderResult = this.senderRetriever.GetSender(transaction, this.coinView, this.blockTxsProcessed);

            if (!getSenderResult.Success)
                throw new ConsensusErrorException(new ConsensusError("sc-consensusvalidator-executecontracttransaction-sender", getSenderResult.Error));

            Script coinbaseScriptPubKey = context.ValidationContext.BlockToValidate.Transactions[0].Outputs[0].ScriptPubKey;

            GetSenderResult getCoinbaseResult = this.senderRetriever.GetAddressFromScript(coinbaseScriptPubKey);

            uint160 coinbaseAddress = (getCoinbaseResult.Success) ? getCoinbaseResult.Sender : uint160.Zero;

            Money mempoolFee = transaction.GetFee(((UtxoRuleContext)context).UnspentOutputSet);

            return new ContractTransactionContext(blockHeight, coinbaseAddress, mempoolFee, getSenderResult.Sender, transaction);
        }
    }
}
