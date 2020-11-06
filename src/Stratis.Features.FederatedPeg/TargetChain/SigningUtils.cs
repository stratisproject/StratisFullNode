using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.FederatedPeg.TargetChain
{
    /// <summary>
    /// Exposes methods that help with the identifying and signing of transactions deterministically.
    /// </summary>
    public static class SigningUtils
    {
        public static int GetSignatureCount(this Transaction partialTransaction, Network network)
        {
            Guard.NotNull(partialTransaction, nameof(partialTransaction));
            Guard.Assert(partialTransaction.Inputs.Any());

            Script scriptSig = partialTransaction.Inputs[0].ScriptSig;
            if (scriptSig == null)
                return 0;

            // Remove the script from the end.
            scriptSig = new Script(scriptSig.ToOps().SkipLast(1));

            TransactionSignature[] result = PayToFederationTemplate.Instance.ExtractScriptSigParameters(network, scriptSig);

            return result?.Count(s => s != null) ?? 0;
        }

        public static Transaction CheckTemplateAndCombineSignatures(TransactionBuilder builder, Transaction existingTransaction, Transaction[] partialTransactions, ILogger logger = null)
        {
            Transaction[] validPartials = partialTransactions.Where(p => TemplatesMatch(builder.Network, p, existingTransaction, logger) && p.GetHash() != existingTransaction.GetHash()).ToArray();
            if (validPartials.Any())
            {
                var allPartials = new Transaction[validPartials.Length + 1];
                allPartials[0] = existingTransaction;
                validPartials.CopyTo(allPartials, 1);

                existingTransaction = builder.CombineSignatures(true, allPartials);
            }

            return existingTransaction;
        }


        /// <summary>
        /// Checks whether two transactions have identical inputs and outputs.
        /// </summary>
        /// <param name="partialTransaction1">First transaction.</param>
        /// <param name="partialTransaction2">Second transaction.</param>
        /// <returns><c>True</c> if identical and <c>false</c> otherwise.</returns>
        public static bool TemplatesMatch(Network network, Transaction partialTransaction1, Transaction partialTransaction2, ILogger logger = null)
        {
            if ((partialTransaction1.Inputs.Count != partialTransaction2.Inputs.Count) ||
                (partialTransaction1.Outputs.Count != partialTransaction2.Outputs.Count))
            {
                logger.LogInformation($"Partial1 inputs:{partialTransaction1.Inputs.Count} = Partial2 inputs:{partialTransaction2.Inputs.Count}");
                logger.LogInformation($"Partial1 Outputs:{partialTransaction1.Outputs.Count} = Partial2 Outputs:{partialTransaction2.Outputs.Count}");
                return false;
            }

            for (int i = 0; i < partialTransaction1.Inputs.Count; i++)
            {
                TxIn input1 = partialTransaction1.Inputs[i];
                TxIn input2 = partialTransaction2.Inputs[i];

                if ((input1.PrevOut.N != input2.PrevOut.N) || (input1.PrevOut.Hash != input2.PrevOut.Hash))
                {
                    logger.LogInformation($"input1 N:{input1.PrevOut.N} = input2 N:{input2.PrevOut.N}");
                    logger.LogInformation($"input1 Hash:{input1.PrevOut.Hash} = input2 Hash:{input2.PrevOut.Hash}");
                    return false;
                }
            }

            for (int i = 0; i < partialTransaction1.Outputs.Count; i++)
            {
                TxOut output1 = partialTransaction1.Outputs[i];
                TxOut output2 = partialTransaction2.Outputs[i];

                if ((output1.Value != output2.Value) || (output1.ScriptPubKey != output2.ScriptPubKey))
                {
                    logger.LogInformation($"output1 Value:{output1.Value} = output2 Value:{output2.Value}");
                    logger.LogInformation($"output1 ScriptPubKey:{output1.ScriptPubKey} = output2 ScriptPubKey:{output2.ScriptPubKey}");

                    return false;
                }
            }

            return true;
        }
    }
}
