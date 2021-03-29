using System;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Consensus.Rules;

namespace Stratis.Bitcoin.Features.Consensus.Interfaces
{
    public interface ISmartContractCoinViewRuleLogic
    {
        /// <summary>
        /// Runs the smart contract coin view rule logic and then runs the base rule logic.
        /// </summary>
        /// <param name="baseRunAsync">The base rule logic.</param>
        /// <param name="context">The rule context.</param>
        Task RunAsync(Func<RuleContext, Task> baseRunAsync, RuleContext context);

        /// <summary>
        /// Executes contracts as necessary and updates the coinview / UTXOset after execution.
        /// </summary>
        /// <param name="baseUpdateUTXOSet">The base rule logic.</param>
        /// <param name="context">The rule context.</param>
        /// <param name="transaction">The transaction.</param>
        void UpdateCoinView(Action<RuleContext, Transaction> baseUpdateUTXOSet, RuleContext context, Transaction transaction);

        /// <summary>
        /// Applies smart contract rules to a given TxOut.
        /// </summary>
        /// <param name="baseCheckInput">The base rule logic.</param>
        /// <param name="tx">The transaction.</param>
        /// <param name="inputIndexCopy">The input index copy.</param>
        /// <param name="txout">The TxOut to check.</param>
        /// <param name="txData">The precomputed transaction data.</param>
        /// <param name="input">The corresponding TxIn.</param>
        /// <param name="flags">The deployment flags.</param>
        /// <returns>Return <c>true</c> if the check passes and <c>false</c> otherwise.</returns>
        bool CheckInput(Func<Transaction, int, TxOut, PrecomputedTransactionData, TxIn, DeploymentFlags, bool> baseCheckInput, Transaction tx, int inputIndexCopy, TxOut txout, PrecomputedTransactionData txData, TxIn input, DeploymentFlags flags);
    }
}
