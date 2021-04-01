using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.SmartContracts.PoS;

namespace Stratis.Bitcoin.Features.SmartContracts.Rules
{
    /// <summary>
    /// Each transaction should have only 1 'SmartContractExec' output.
    /// </summary>
    public class TxOutSmartContractExecRule : FullValidationConsensusRule
    {
        private readonly ISmartContractActivationProvider smartContractActivationProvider;

        public TxOutSmartContractExecRule(ISmartContractActivationProvider smartContractActivationProvider = null)
        {
            this.smartContractActivationProvider = smartContractActivationProvider;
        }

        /// <inheritdoc/>
        public override Task RunAsync(RuleContext context)
        {
            if (!(this.smartContractActivationProvider?.IsRuleApplicable(context) ?? true))
                return Task.CompletedTask;

            Block block = context.ValidationContext.BlockToValidate;

            foreach (Transaction transaction in block.Transactions)
            {
                CheckTransaction(transaction);
            }

            return Task.CompletedTask;
        }

        public static void CheckTransaction(Transaction transaction)
        {
            int smartContractExecCount = transaction.Outputs.Count(o => o.ScriptPubKey.IsSmartContractExec());

            if ((transaction.IsCoinBase) && smartContractExecCount > 0)
                new ConsensusError("smartcontractexec-in-coinbase", "coinbase contains smartcontractexec output").Throw();

            if (smartContractExecCount > 1)
                new ConsensusError("multiple-smartcontractexec-outputs", "transaction contains multiple smartcontractexec outputs").Throw();
        }
    }
}
