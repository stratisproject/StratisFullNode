using Stratis.Bitcoin.Configuration;
using Stratis.Bitcoin.Features.Interop.Models;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Bitcoin.Features.Interop.Settings
{
    public sealed class CirrusInteropSettings : ETHInteropSettings
    {
        /// <summary>This is the URL of the Cirrus node's API, for actioning SRC20 contract calls.</summary>
        public string CirrusClientUrl { get; set; }

        public string CirrusSmartContractActiveAddress { get; set; }

        public string CirrusMultisigContractAddress { get; set; }

        public WalletCredentials CirrusWalletCredentials { get; set; }

        public CirrusInteropSettings(NodeSettings nodeSettings) : base(nodeSettings)
        {
            this.CirrusClientUrl = nodeSettings.ConfigReader.GetOrDefault("cirrusclienturl", nodeSettings.Network.IsTest() ? "http://localhost:38223" : "http://localhost:37223");
            this.CirrusSmartContractActiveAddress = nodeSettings.ConfigReader.GetOrDefault<string>("cirrussmartcontractactiveaddress", null);
            this.CirrusMultisigContractAddress = nodeSettings.ConfigReader.GetOrDefault<string>("cirrusmultisigcontractaddress", null);
        }
    }
}
