using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin.BitcoinCore;
using NBitcoin.Policy;

namespace NBitcoin
{
    /// <summary>
    /// Injected proxy to <see cref="StandardScripts"/>.
    /// </summary>
    public class StandardScriptsRegistry : IStandardScriptsRegistry
    {
        /// <summary>
        /// Registers a new standard script template if it does not exist yet based on <see cref="ScriptTemplate.Type"/>.
        /// </summary>
        /// <param name="scriptTemplate">The standard script template to register.</param>
        public virtual void RegisterStandardScriptTemplate(ScriptTemplate scriptTemplate)
        {
            if (!this.GetScriptTemplates.Any(template => (template.Type == scriptTemplate.Type)))
                this.GetScriptTemplates.Add(scriptTemplate);
        }

        public ScriptTemplate this[Type key]
        {
            get => this.GetScriptTemplates.FirstOrDefault(template => template.GetType() == key);
        }

        public virtual bool IsStandardTransaction(Transaction tx, Network network, int blockHeight = -1, uint256 blockHash = null)
        {
            return new StandardTransactionPolicy(network).Check(tx, null, blockHeight, blockHash).Length == 0;
        }

        public virtual bool AreOutputsStandard(Network network, Transaction tx)
        {
            return tx.Outputs.All(vout => this.IsStandardScriptPubKey(network, vout.ScriptPubKey));
        }

        public virtual ScriptTemplate GetTemplateFromScriptPubKey(Script script)
        {
            return this.GetScriptTemplates.FirstOrDefault(t => t.CheckScriptPubKey(script));
        }

        public virtual bool IsStandardScriptPubKey(Network network, Script scriptPubKey)
        {
            return this.GetScriptTemplates.Any(template => template.CheckScriptPubKey(scriptPubKey));
        }

        public virtual bool IsStandardScriptSig(Network network, Script scriptSig, Script scriptPubKey = null)
        {
            if (scriptPubKey == null)
                return this.GetScriptTemplates.Any(x => x.CheckScriptSig(network, scriptSig, null));

            ScriptTemplate template = this.GetTemplateFromScriptPubKey(scriptPubKey);
            if (template == null)
                return false;

            return template.CheckScriptSig(network, scriptSig, scriptPubKey);
        }

        // Check transaction inputs, and make sure any
        // pay-to-script-hash transactions are evaluating IsStandard scripts
        //
        // Why bother? To avoid denial-of-service attacks; an attacker
        // can submit a standard HASH... OP_EQUAL transaction,
        // which will get accepted into blocks. The redemption
        // script can be anything; an attacker could use a very
        // expensive-to-check-upon-redemption script like:
        //   DUP CHECKSIG DROP ... repeated 100 times... OP_1
        public virtual bool AreInputsStandard(Network network, Transaction tx, CoinsView coinsView)
        {
            if (tx.IsCoinBase)
                return true; // Coinbases don't use vin normally

            foreach (TxIn input in tx.Inputs)
            {
                TxOut prev = coinsView.GetOutputFor(input);
                if (prev == null)
                    return false;

                if (!this.IsStandardScriptSig(network, input.ScriptSig, prev.ScriptPubKey))
                    return false;
            }

            return true;
        }

        public virtual List<ScriptTemplate> GetScriptTemplates
        {
            get { throw new NotImplementedException(); }
        }
    }
}
