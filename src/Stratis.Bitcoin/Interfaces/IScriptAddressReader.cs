using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace Stratis.Bitcoin.Interfaces
{
    /// <summary>
    /// A reader for extracting an address from a Script
    /// </summary>
    public interface IScriptAddressReader
    {
        /// <summary>
        /// Extracts an address from a given Script, if available. Otherwise returns <see cref="string.Empty"/>
        /// </summary>
        /// <param name="network"></param>
        /// <param name="script"></param>
        /// <returns></returns>
        string GetAddressFromScriptPubKey(Network network, Script script);
    }

    public interface IScriptDestinationReader : IScriptAddressReader
    {
        IEnumerable<TxDestination> GetDestinationFromScriptPubKey(Network network, Script script);
    }

    public static class IScriptAddressReaderExt
    {
        public static IEnumerable<TxDestination> GetDestinationFromScriptPubKey(this IScriptAddressReader scriptAddressReader, Network network, Script redeemScript)
        {
            ScriptTemplate scriptTemplate = network.StandardScriptsRegistry.GetTemplateFromScriptPubKey(redeemScript);

            if (scriptTemplate != null)
            {
                // We need scripts suitable for matching to HDAddress.ScriptPubKey.
                switch (scriptTemplate.Type)
                {
                    case TxOutType.TX_PUBKEYHASH:
                        yield return PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(redeemScript);
                        break;
                    case TxOutType.TX_PUBKEY:
                        yield return PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(redeemScript).Hash;
                        break;
                    case TxOutType.TX_SCRIPTHASH:
                        yield return PayToScriptHashTemplate.Instance.ExtractScriptPubKeyParameters(redeemScript);
                        break;
                    case TxOutType.TX_SEGWIT:
                        TxDestination txDestination = PayToWitTemplate.Instance.ExtractScriptPubKeyParameters(network, redeemScript);
                        if (txDestination != null)
                            yield return new KeyId(txDestination.ToBytes());
                        break;
                    default:
                        if (scriptAddressReader is IScriptDestinationReader scriptDestinationReader)
                        {
                            foreach (TxDestination destination in scriptDestinationReader.GetDestinationFromScriptPubKey(network, redeemScript))
                            {
                                yield return destination;
                            }
                        }
                        else
                        {
                            TxDestination GetDestinationForAddress(string address)
                            {
                                if (address == null)
                                    return null;

                                byte[] decoded = Encoders.Base58Check.DecodeData(address);
                                return new KeyId(new uint160(decoded.Skip(network.GetVersionBytes(Base58Type.PUBKEY_ADDRESS, true).Length).ToArray()));
                            }

                            string address = scriptAddressReader.GetAddressFromScriptPubKey(network, redeemScript);
                            TxDestination destination = GetDestinationForAddress(address);
                            if (destination != null)
                                yield return destination;
                        }

                        break;
                }
            }
        }
    }
}
