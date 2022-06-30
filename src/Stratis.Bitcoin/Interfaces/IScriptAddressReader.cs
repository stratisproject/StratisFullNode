using System.Collections.Generic;
using NBitcoin;

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
        /// <param name="scriptTemplate">The appropriate template for this type of script.</param>
        /// <param name="network">The network.</param>
        /// <param name="script">The script.</param>
        /// <returns></returns>
        string GetAddressFromScriptPubKey(ScriptTemplate scriptTemplate, Network network, Script script);

        IEnumerable<TxDestination> GetDestinationFromScriptPubKey(ScriptTemplate scriptTemplate, Script redeemScript);
    }
}
