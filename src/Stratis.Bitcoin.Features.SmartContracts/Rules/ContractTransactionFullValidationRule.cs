using System.Collections.Generic;
using System.Threading.Tasks;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.Bitcoin.Features.SmartContracts.PoS;
using Stratis.SmartContracts.CLR;

namespace Stratis.Bitcoin.Features.SmartContracts.Rules
{
    /// <summary>
    /// Checks that smart contract transactions are in a valid format and the data is serialized correctly.
    /// </summary>
    public class ContractTransactionFullValidationRule : FullValidationConsensusRule
    {
        private readonly ContractTransactionChecker transactionChecker;
        private readonly ISmartContractActivationProvider smartContractActivationProvider;

        /// <summary>The rules are kept in a covariant interface.</summary>
        private readonly IEnumerable<IContractTransactionFullValidationRule> internalRules;

        public ContractTransactionFullValidationRule(ICallDataSerializer serializer, IEnumerable<IContractTransactionFullValidationRule> internalRules, ISmartContractActivationProvider smartContractActivationProvider = null)
        {
            this.transactionChecker = new ContractTransactionChecker(serializer);
            this.smartContractActivationProvider = smartContractActivationProvider;

            this.internalRules = internalRules;
        }

        /// <inheritdoc/>
        public override Task RunAsync(RuleContext context)
        {
            if (this.smartContractActivationProvider?.SkipRule(context) ?? false)
                return Task.CompletedTask;

            return this.transactionChecker.RunAsync(context, this.internalRules);
        }
    }
}