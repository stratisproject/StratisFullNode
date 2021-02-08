using System.Threading.Tasks;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Consensus.Rules;
using Stratis.SmartContracts.Core;

namespace Stratis.Bitcoin.Features.SmartContracts.Rules
{
    /// <summary>
    /// Enforces that only certain script types are used on the network.
    /// </summary>
    public class AllowedScriptTypeRule : PartialValidationConsensusRule
    {
        private readonly Network network;

        public AllowedScriptTypeRule(Network network)
        {
            this.network = network;
        }

        /// <inheritdoc/>
        public override Task RunAsync(RuleContext context)
        {
            Block block = context.ValidationContext.BlockToValidate;

            foreach (Transaction transaction in block.Transactions)
            {
                CheckTransaction(this.network, transaction, context.ValidationContext.ChainedHeaderToValidate.Height, context.ValidationContext.ChainedHeaderToValidate.HashBlock);
            }

            return Task.CompletedTask;
        }

        public static void CheckTransaction(Network network, Transaction transaction, int blockHeight = -1, uint256 blockHash = null)
        {
            // Why dodge coinbase?
            // 1) Coinbase can only be written by Authority nodes anyhow.
            // 2) Coinbase inputs look weird, are tough to validate.
            if (!transaction.IsCoinBase)
            {
                foreach (TxOut output in transaction.Outputs)
                {
                    CheckOutput(network, output, blockHeight, blockHash);
                }

                foreach (TxIn input in transaction.Inputs)
                {
                    CheckInput(network, input);
                }
            }
        }

        private static void CheckOutput(Network network, TxOut output, int blockHeight, uint256 blockHash)
        {
            if (output.ScriptPubKey.IsSmartContractExec())
                return;

            if (output.ScriptPubKey.IsSmartContractInternalCall())
                return;

            Script script = (blockHeight < 0) ? output.ScriptPubKey : new ScriptAtHeight(output.ScriptPubKey, blockHeight, blockHash);

            // Pay to side chain miner.	
            if (PayToPubkeyHashTemplate.Instance.CheckScriptPubKey(script))
                return;

            // For cross-chain transfers	
            if (PayToScriptHashTemplate.Instance.CheckScriptPubKey(script))
                return;

            // For cross-chain transfers	
            if (PayToMultiSigTemplate.Instance.CheckScriptPubKey(script))
                return;

            // For cross-chain transfers	
            if (PayToFederationTemplate.Instance.CheckScriptPubKey(script))
                return;

            // For cross-chain transfers	
            if (network.StandardScriptsRegistry[typeof(TxNullDataTemplate)].CheckScriptPubKey(script))
                return;

            new ConsensusError("disallowed-output-script", $"Only the following script types are allowed on smart contracts network: P2PKH, P2SH, P2MultiSig, OP_RETURN and smart contracts.").Throw();
        }

        private static void CheckInput(Network network, TxIn input)
        {
            if (input.ScriptSig.IsSmartContractSpend())
                return;

            if (PayToPubkeyHashTemplate.Instance.CheckScriptSig(network, input.ScriptSig))
                return;

            // Currently necessary to spend premine. Could be stricter.	
            if (PayToPubkeyTemplate.Instance.CheckScriptSig(network, input.ScriptSig, null))
                return;

            if (PayToScriptHashTemplate.Instance.CheckScriptSig(network, input.ScriptSig, null))
                return;

            // For cross-chain transfers	
            if (PayToMultiSigTemplate.Instance.CheckScriptSig(network, input.ScriptSig, null))
                return;

            // For cross-chain transfers	
            if (PayToFederationTemplate.Instance.CheckScriptSig(network, input.ScriptSig, null))
                return;

            new ConsensusError("disallowed-input-script", "Only the following script types are allowed on smart contracts network: P2PKH, P2SH, P2MultiSig, OP_RETURN and smart contracts").Throw();
        }
    }
}
