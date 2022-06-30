using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Consensus;
using Stratis.Bitcoin.Interfaces;

namespace Stratis.Bitcoin.Features.ColdStaking
{
    /// <summary>
    /// This class offers the ability to selectively replace <see cref="ScriptAddressReader"/>
    /// which can only parse a single address from a ScriptPubKey. ColdStaking scripts contain two addresses / pub key hashes.
    /// </summary>
    public class ColdStakingDestinationReader : IScriptAddressReader
    {
        IScriptAddressReader scriptAddressReader;

        public ColdStakingDestinationReader(IScriptAddressReader scriptAddressReader)
        {
            this.scriptAddressReader = scriptAddressReader;
        }

        public string GetAddressFromScriptPubKey(ScriptTemplate scriptTemplate, Network network, Script script)
        {
            return this.scriptAddressReader.GetAddressFromScriptPubKey(scriptTemplate, network, script);
        }

        public IEnumerable<TxDestination> GetDestinationFromScriptPubKey(ScriptTemplate scriptTemplate, Script script)
        {
            if (scriptTemplate.Type == TxOutType.TX_COLDSTAKE && ((ColdStakingScriptTemplate)scriptTemplate).ExtractScriptPubKeyParameters(script, out KeyId hotPubKeyHash, out KeyId coldPubKeyHash))
            {
                IEnumerable<TxDestination> GetDestinations()
                {
                    yield return hotPubKeyHash;
                    yield return coldPubKeyHash;
                }

                return GetDestinations();
            }

            return this.scriptAddressReader.GetDestinationFromScriptPubKey(scriptTemplate, script);
        }
    }
}
