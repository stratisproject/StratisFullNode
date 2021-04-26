using System;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Base.Deployments;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.Consensus.Interfaces;

namespace Stratis.Features.SystemContracts
{
    public class SystemContractCoinViewRuleLogic : ISmartContractCoinViewRuleLogic
    {
        public bool CheckInput(Func<Transaction, int, TxOut, PrecomputedTransactionData, TxIn, DeploymentFlags, bool> baseCheckInput, Transaction tx, int inputIndexCopy, TxOut txout, PrecomputedTransactionData txData, TxIn input, DeploymentFlags flags)
        {
            throw new NotImplementedException();
        }

        public Task RunAsync(Func<RuleContext, Task> baseRunAsync, RuleContext context)
        {
            throw new NotImplementedException();
        }

        public void UpdateCoinView(Action<RuleContext, Transaction> baseUpdateUTXOSet, RuleContext context, Transaction transaction)
        {
            throw new NotImplementedException();
        }
    }
}
