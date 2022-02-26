using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
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

    public static class CirrusContractAddresses
    {
        private static Dictionary<NetworkType, List<CirrusContractAddress>> contractAddresses = new Dictionary<NetworkType, List<CirrusContractAddress>>()
        {
            {
                NetworkType.Testnet,
                new List<CirrusContractAddress>()
                {
                    new CirrusContractAddress()
                    {
                        TokenName = "TST2",
                        NetworkName = "CirrusTest",
                        ERC20Address = "0xf197f5f8c406d269e2cc44aaf495fbc4eb519634",
                        SRC20Address = "tNVR1r6WSWSCK7XVQsz9aJk3CdBGGvFgY5"
                    },
                    new CirrusContractAddress()
                    {
                        TokenName = "TST3",
                        NetworkName = "CirrusTest",
                        ERC20Address = "0xa3c22370de5f9544f0c4de126b1e46ceadf0a51b",
                        SRC20Address = "tWD9SN1hPcbefQAWarkB7Jg4xjkDkBYjvH"
                    },
                    new CirrusContractAddress()
                    {
                        TokenName = "TST4",
                        NetworkName = "CirrusTest",
                        ERC20Address = "0x5da5cfe7d4ce1cc0712ebc0bb58eff93817a6801",
                        SRC20Address = "tQhh41Z6LCbiJEF78c2myQHqvbptsfPmSu"
                    },
                    new CirrusContractAddress()
                    {
                        TokenName = "TST5",
                        NetworkName = "CirrusTest",
                        ERC20Address = "0x14f768657135d3daafb45d242157055f1c9143f3",
                        SRC20Address = "tJTNQT4inhr9PPZ7RF8B2tX5RaN1AdK2cp"
                    }
                }
            },
            {
                NetworkType.Mainnet,
                new List<CirrusContractAddress>()
                {
                }
            },
        };

        public static List<CirrusContractAddress> GetForNetwork(NetworkType networkType)
        {
            return contractAddresses.GetValueOrDefault(networkType);
        }
    }

    public sealed class CirrusContractAddress
    {
        [JsonProperty(PropertyName = "tokenName")]
        public string TokenName { get; set; }

        [JsonProperty(PropertyName = "networkName")]
        public string NetworkName { get; set; }

        [JsonProperty(PropertyName = "erc20Address")]
        public string ERC20Address { get; set; }

        [JsonProperty(PropertyName = "src20Address")]
        public string SRC20Address { get; set; }
    }
}
