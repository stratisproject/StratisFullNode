using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;

namespace Stratis.Bitcoin.Features.Consensus.Rules.CommonRules
{
    /// <summary>Validate a transaction that withdraws funds from a federation multisig.
    /// The usual multisig opcodes aren't suitable for this as the federation membership can change over time.
    /// So a specialised opcode is introduced that indicates to this rule that more complex processing is required.</summary>
    public class FederationWithdrawalRule : PartialValidationConsensusRule
    {
        /// <inheritdoc />
        public override Task RunAsync(RuleContext context)
        {
            if (context.SkipValidation)
                return Task.CompletedTask;

            Block block = context.ValidationContext.BlockToValidate;

            // Check transactions
            foreach (Transaction tx in block.Transactions)
                this.CheckTransaction(tx);

            return Task.CompletedTask;
        }

        public virtual void CheckTransaction(Transaction transaction)
        {
            if (transaction.IsFederationWithdrawal)
            {
                foreach (TxOut txout in transaction.Outputs)
                {
                    // Get the output(s) of the transaction that are federation withdrawals
                    // Get the federation identifier
                    // Validate that it is a known identifier (this potentially allows for multiple independent federations to exist simultaneously)
                    // Retrieve the list of acceptable pubkeys for the given federation
                    // Check that an acceptable number of them have valid signatures in the scriptSig -> we can't do all of this within the script evaluation as the script engine has no concept of the validity of the 'allowed' pubkeys
                }
            }
        }
    }
}