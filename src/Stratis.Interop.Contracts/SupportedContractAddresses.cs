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
                        NativeNetworkAddress = "0xe4a444cb3222fd8e9518db8f70a33aadb9a1a358",
                        SRC20Address = "tSnYKnLSEjFVYgMC5ajDxy3iuGd4boe3NA",
                        TokenName = "TSZ1",
                        Decimals = 18
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xf197f5f8c406d269e2cc44aaf495fbc4eb519634",
                        SRC20Address = "tMZ2dKG9BKvCfVWvxUN47enXyaDyWLe44z",
                        TokenName = "TSZ2",
                        Decimals = 8
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xa3c22370de5f9544f0c4de126b1e46ceadf0a51b",
                        SRC20Address = "tECnjt1eYCxK7wztSS92QQvKzqWAbpzfXt",
                        TokenName = "TSZ3",
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
