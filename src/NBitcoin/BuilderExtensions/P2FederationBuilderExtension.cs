using System;
using System.Collections.Generic;
using System.Linq;

namespace NBitcoin.BuilderExtensions
{
    public class P2FederationBuilderExtension : BuilderExtension
    {
        public override bool CanCombineScriptSig(Network network, Script scriptPubKey, Script a, Script b)
        {
            return PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey) != null;
        }

        public override bool CanDeduceScriptPubKey(Network network, Script scriptSig)
        {
            return false;
        }

        public override bool CanEstimateScriptSigSize(Network network, Script scriptPubKey)
        {
            return PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey) != null;
        }

        public override bool CanGenerateScriptSig(Network network, Script scriptPubKey)
        {
            return PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey) != null;
        }

        public override Script CombineScriptSig(Network network, Script scriptPubKey, Script a, Script b)
        {
            byte[] federationId = PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey);
            (PubKey[] pubKeys, int signatureCount) = network.Federation.GetFederationDetails(federationId);

            // Combine all the signatures we've got:
            TransactionSignature[] aSigs = PayToFederationTemplate.Instance.ExtractScriptSigParameters(network, a);
            if (aSigs == null)
                return b;
            TransactionSignature[] bSigs = PayToFederationTemplate.Instance.ExtractScriptSigParameters(network, b);
            if (bSigs == null)
                return a;
            int sigCount = 0;
            var sigs = new TransactionSignature[pubKeys.Length];
            for (int i = 0; i < pubKeys.Length; i++)
            {
                TransactionSignature aSig = i < aSigs.Length ? aSigs[i] : null;
                TransactionSignature bSig = i < bSigs.Length ? bSigs[i] : null;
                TransactionSignature sig = aSig ?? bSig;
                if (sig != null)
                {
                    sigs[i] = sig;
                    sigCount++;
                }
                if (sigCount == signatureCount)
                    break;
            }
            if (sigCount == signatureCount)
                sigs = sigs.Where(s => s != null && s != TransactionSignature.Empty).ToArray();
            return PayToFederationTemplate.Instance.GenerateScriptSig(sigs);
        }

        public override Script DeduceScriptPubKey(Network network, Script scriptSig)
        {
            throw new NotImplementedException();
        }

        public override int EstimateScriptSigSize(Network network, Script scriptPubKey)
        {
            byte[] federationId = PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey);
            (PubKey[] pubKeys, int sigsReq) = network.Federation.GetFederationDetails(federationId);
            return PayToFederationTemplate.Instance.GenerateScriptSig(Enumerable.Range(0, sigsReq).Select(o => DummySignature).ToArray()).Length;
        }

        public override Script GenerateScriptSig(Network network, Script scriptPubKey, IKeyRepository keyRepo, ISigner signer)
        {
            byte[] federationId = PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey);
            (PubKey[] pubKeys, int sigsReq) = network.Federation.GetFederationDetails(federationId);
            var signatures = new TransactionSignature[pubKeys.Length];
            Key[] keys =
                pubKeys
                .Select(p => keyRepo.FindKey(p.ScriptPubKey))
                .ToArray();

            int sigCount = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                if (sigCount == sigsReq)
                    break;
                if (keys[i] != null)
                {
                    TransactionSignature sig = signer.Sign(keys[i]);
                    signatures[i] = sig;
                    sigCount++;
                }
            }

            IEnumerable<TransactionSignature> sigs = signatures;
            if (sigCount == sigsReq)
            {
                sigs = sigs.Where(s => s != TransactionSignature.Empty && s != null);
            }
            return PayToMultiSigTemplate.Instance.GenerateScriptSig(sigs);
        }
    }
}
