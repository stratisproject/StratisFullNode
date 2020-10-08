using System.Text;
using NBitcoin;

namespace FederationSetup
{
    public class MultisigAddressCreator
    {
        public string CreateMultisigAddresses(Network mainchainNetwork, Network sidechainNetwork)
        {
            var output = new StringBuilder();

            Script payToMultiSig = PayToFederationTemplate.Instance.GenerateScriptPubKey(sidechainNetwork.Federations.GetOnlyFederation().Id);
            output.AppendLine("Redeem script: " + payToMultiSig.ToString());

            BitcoinAddress sidechainMultisigAddress = payToMultiSig.Hash.GetAddress(sidechainNetwork);
            output.AppendLine("Sidechan P2SH: " + sidechainMultisigAddress.ScriptPubKey);
            output.AppendLine("Sidechain Multisig address: " + sidechainMultisigAddress);

            BitcoinAddress mainchainMultisigAddress = payToMultiSig.Hash.GetAddress(mainchainNetwork);
            output.AppendLine("Mainchain P2SH: " + mainchainMultisigAddress.ScriptPubKey);
            output.AppendLine("Mainchain Multisig address: " + mainchainMultisigAddress);

            return output.ToString();
        }
    }
}