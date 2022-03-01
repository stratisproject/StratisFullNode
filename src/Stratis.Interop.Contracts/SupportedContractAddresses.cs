using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;

namespace Stratis.Interop.Contracts
{
    public static class SupportedContractAddresses
    {
        private static Dictionary<NetworkType, List<SupportedContractAddress>> contractAddresses = new Dictionary<NetworkType, List<SupportedContractAddress>>()
        {
            {
                NetworkType.Testnet,
                new List<SupportedContractAddress>()
                {
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xf197f5f8c406d269e2cc44aaf495fbc4eb519634",
                        SRC20Address = "tNVR1r6WSWSCK7XVQsz9aJk3CdBGGvFgY5",
                        TokenName = "TST2",
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xa3c22370de5f9544f0c4de126b1e46ceadf0a51b",
                        SRC20Address = "tWD9SN1hPcbefQAWarkB7Jg4xjkDkBYjvH",
                        TokenName = "TST3",
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x5da5cfe7d4ce1cc0712ebc0bb58eff93817a6801",
                        SRC20Address = "tQhh41Z6LCbiJEF78c2myQHqvbptsfPmSu",
                        TokenName = "TST4",
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x14f768657135d3daafb45d242157055f1c9143f3",
                        SRC20Address = "tJTNQT4inhr9PPZ7RF8B2tX5RaN1AdK2cp",
                        TokenName = "TST5",
                    }
                }
            },
            {
                NetworkType.Mainnet,
                new List<SupportedContractAddress>()
                {
                }
            },
        };

        public static List<SupportedContractAddress> ForNetwork(NetworkType networkType)
        {
            return contractAddresses.GetValueOrDefault(networkType);
        }
    }

    public enum SupportedNativeChain
    {
        Ethereum
    }

    public sealed class SupportedContractAddress
    {
        [JsonProperty(PropertyName = "nativeNetwork")]
        public SupportedNativeChain NativeNetwork { get; set; }

        [JsonProperty(PropertyName = "nativeChainAddress")]
        public string NativeNetworkAddress { get; set; }

        [JsonProperty(PropertyName = "src20Address")]
        public string SRC20Address { get; set; }

        [JsonProperty(PropertyName = "tokenName")]
        public string TokenName { get; set; }
    }
}
