using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

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
                        NativeNetworkAddress = "0xf5dab0f35378ea5fc69149d0f20ba0c16b170a3d",
                        SRC20Address = "tQk6t6ithWWoBUQxphDShcYFF6s916mM4R",
                        TokenName = "TSTX",
                        Decimals = 18
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x2b3b0bd8219ffe0c22ffcdefbc81b7efa5c8d9ba",
                        SRC20Address = "tWCCJ3FxmoYuzrE4aUcDLDh9gn51EJ4cvM",
                        TokenName = "TSTY",
                        Decimals = 8
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x4cb3e0b719a7707c0148e21585d8011213de6708",
                        SRC20Address = "tQspjyuEap2vDaNkf9KRHQLdU3h8qq6dnX",
                        TokenName = "TSTZ",
                        Decimals = 6
                    },
                }
            },
            {
                NetworkType.Mainnet,
                new List<SupportedContractAddress>()
                {
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2",
                        SRC20Address = "CLgwsVta6KhALRGGv8mtq8ECEAudrhVJT1",
                        TokenName = "WETH",
                        Decimals = 18
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x2260FAC5E5542a773Aa44fBCfeDf7C193bc2C599",
                        SRC20Address = "CPWWnnDJQXcJvP4MCNRhhPWyFcBfv2xnoe",
                        TokenName = "WBTC",
                        Decimals = 8
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48",
                        SRC20Address = "CeN35f2EeDE7gRNjbwDiVHUphNRB5eBUDh",
                        TokenName = "USDC",
                        Decimals = 6
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xdAC17F958D2ee523a2206206994597C13D831ec7",
                        SRC20Address = "Cf8CJMFADmkLuRNpMfHGk5agJdin3XD8UR",
                        TokenName = "USDT",
                        Decimals = 6
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x514910771AF9Ca656af840dff83E8264EcF986CA",
                        SRC20Address = "CHDyDnoUGvAB9hjLmxXymwWW2WWUmNGRf3",
                        TokenName = "LINK",
                        Decimals = 18
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x95aD61b0a150d79219dCF64E1E6Cc01f0B64C4cE",
                        SRC20Address = "CTqXKirw9qjLWSmbuB9Az53hqGYQ6FCewE",
                        TokenName = "SHIB",
                        Decimals = 18
                    }
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
        [JsonConverter(typeof(StringEnumConverter))]
        public SupportedNativeChain NativeNetwork { get; set; }

        [JsonProperty(PropertyName = "nativeChainAddress")]
        public string NativeNetworkAddress { get; set; }

        [JsonProperty(PropertyName = "src20Address")]
        public string SRC20Address { get; set; }

        [JsonProperty(PropertyName = "tokenName")]
        public string TokenName { get; set; }

        [JsonProperty(PropertyName = "decimals")]
        public int Decimals { get; set; }
    }
}
