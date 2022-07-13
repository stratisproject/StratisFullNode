using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Interfaces;

namespace Stratis.Bitcoin.Consensus
{
    public static class ScriptAddressReaderExt
    {
        public static string GetAddressFromScriptPubKey(this IScriptAddressReader scriptAddressReader, Network network, Script script)
        {
            ScriptTemplate scriptTemplate = network.StandardScriptsRegistry.GetTemplateFromScriptPubKey(script);
            if (scriptTemplate == null)
                return null;

            return scriptAddressReader.GetAddressFromScriptPubKey(scriptTemplate, network, script);
        }

        public static IEnumerable<TxDestination> GetDestinationFromScriptPubKey(this IScriptAddressReader scriptAddressReader, Network network, Script script)
        {
            ScriptTemplate scriptTemplate = network.StandardScriptsRegistry.GetTemplateFromScriptPubKey(script);
            if (scriptTemplate == null)
                return new TxDestination[0];

            return scriptAddressReader.GetDestinationFromScriptPubKey(scriptTemplate, script);
        }
    }

    /// <inheritdoc cref="IScriptAddressReader"/>
    public class ScriptAddressReader : IScriptAddressReader
    {
        /// <inheritdoc cref="IScriptAddressReader.GetAddressFromScriptPubKey"/>
        public string GetAddressFromScriptPubKey(ScriptTemplate scriptTemplate, Network network, Script script)
        {
            string destinationAddress = null;

            switch (scriptTemplate.Type)
            {
                // Pay to PubKey can be found in outputs of staking transactions.
                case TxOutType.TX_PUBKEY:
                    PubKey pubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(script);
                    destinationAddress = pubKey.GetAddress(network).ToString();
                    break;
                // Pay to PubKey hash is the regular, most common type of output.
                case TxOutType.TX_PUBKEYHASH:
                case TxOutType.TX_SCRIPTHASH:
                case TxOutType.TX_SEGWIT:
                    destinationAddress = script.GetDestinationAddress(network).ToString();
                    break;
            }

            return destinationAddress;
        }

        public IEnumerable<TxDestination> GetDestinationFromScriptPubKey(ScriptTemplate scriptTemplate, Script script)
        {
            switch (scriptTemplate.Type)
            {
                case TxOutType.TX_PUBKEYHASH:
                    yield return PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(script);
                    break;
                case TxOutType.TX_PUBKEY:
                    yield return PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(script).Hash;
                    break;
                case TxOutType.TX_SCRIPTHASH:
                    yield return PayToScriptHashTemplate.Instance.ExtractScriptPubKeyParameters(script);
                    break;
                case TxOutType.TX_SEGWIT:
                    TxDestination txDestination = PayToWitTemplate.Instance.ExtractScriptPubKeyParameters(script);
                    if (txDestination != null)
                        yield return new KeyId(txDestination.ToBytes());
                    break;
            }
        }
    }
}
