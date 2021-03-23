using System;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Consensus.Rules;

namespace Stratis.Bitcoin.Features.Consensus.Interfaces
{
    public interface ISmartContractCoinViewRuleLogic
    {
        Task RunAsync(Func<RuleContext, Task> baseRunAsync, RuleContext context);

        /// <summary>
        /// Executes contracts as necessary and updates the coinview / UTXOset after execution.
        /// </summary>
        void UpdateCoinView(Action<RuleContext, Transaction> baseUpdateUTXOSet, RuleContext context, Transaction transaction);

        bool CheckInput(Func<Transaction, int, TxOut, PrecomputedTransactionData, TxIn, DeploymentFlags, bool> baseCheckInput, Transaction tx, int inputIndexCopy, TxOut txout, PrecomputedTransactionData txData, TxIn input, DeploymentFlags flags);
    }
}
