using System;
using System.Collections.Generic;
using System.Linq;

namespace NBitcoin.BuilderExtensions
{
    public class P2FederationBuilderExtension : BuilderExtension
    {
        public override bool CanCombineScriptSig(Network network, Script scriptPubKey, Script a, Script b)
        {
            return PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey, network) != null;
        }

        public override bool CanDeduceScriptPubKey(Network network, Script scriptSig)
        {
            return false;
        }

        public override bool CanEstimateScriptSigSize(Network network, Script scriptPubKey)
        {
            return PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey, network) != null;
        }

        public override bool CanGenerateScriptSig(Network network, Script scriptPubKey)
        {
            return PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey, network) != null;
        }

        public override Script CombineScriptSig(Network network, Script scriptPubKey, Script a, Script b)
        {
            PayToMultiSigTemplateParameters para = PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey, network);

            // Combine all the signatures we've got:
            TransactionSignature[] aSigs = PayToFederationTemplate.Instance.ExtractScriptSigParameters(network, a);
            if (aSigs == null)
                return b;
            TransactionSignature[] bSigs = PayToFederationTemplate.Instance.ExtractScriptSigParameters(network, b);
            if (bSigs == null)
                return a;
            int sigCount = 0;
            var sigs = new TransactionSignature[para.PubKeys.Length];
            for (int i = 0; i < para.PubKeys.Length; i++)
            {
                TransactionSignature aSig = i < aSigs.Length ? aSigs[i] : null;
                TransactionSignature bSig = i < bSigs.Length ? bSigs[i] : null;
                TransactionSignature sig = aSig ?? bSig;
                if (sig != null)
                {
                    sigs[i] = sig;
                    sigCount++;
                }
                if (sigCount == para.SignatureCount)
                    break;
            }
            if (sigCount == para.SignatureCount)
                sigs = sigs.Where(s => s != null && s != TransactionSignature.Empty).ToArray();
            return PayToFederationTemplate.Instance.GenerateScriptSig(sigs);
        }

        public override Script DeduceScriptPubKey(Network network, Script scriptSig)
        {
            throw new NotImplementedException();
        }

        public override int EstimateScriptSigSize(Network network, Script scriptPubKey)
        {
            PayToMultiSigTemplateParameters para = PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey, network);
            return PayToFederationTemplate.Instance.GenerateScriptSig(Enumerable.Range(0, para.SignatureCount).Select(o => DummySignature).ToArray()).Length;
        }

        public override Script GenerateScriptSig(Network network, Script scriptPubKey, IKeyRepository keyRepo, ISigner signer)
        {
            PayToMultiSigTemplateParameters para = PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey, network);
            var signatures = new TransactionSignature[para.PubKeys.Length];
            Key[] keys =
                para.PubKeys
                .Select(p => keyRepo.FindKey(p.ScriptPubKey))
                .ToArray();

            int sigCount = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                if (sigCount == para.SignatureCount)
                    break;
                if (keys[i] != null)
                {
                    TransactionSignature sig = signer.Sign(keys[i]);
                    signatures[i] = sig;
                    sigCount++;
                }
            }

            IEnumerable<TransactionSignature> sigs = signatures;
            if (sigCount == para.SignatureCount)
            {
                sigs = sigs.Where(s => s != TransactionSignature.Empty && s != null);
            }
            return PayToFederationTemplate.Instance.GenerateScriptSig(sigs);
        }
    }
}
