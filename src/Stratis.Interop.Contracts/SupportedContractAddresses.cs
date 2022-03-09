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
                        NativeNetworkAddress = "0xf197f5f8c406d269e2cc44aaf495fbc4eb519634",
                        SRC20Address = "tNVR1r6WSWSCK7XVQsz9aJk3CdBGGvFgY5",
                        TokenName = "TST2",
                        Decimals = 18
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xa3c22370de5f9544f0c4de126b1e46ceadf0a51b",
                        SRC20Address = "tWD9SN1hPcbefQAWarkB7Jg4xjkDkBYjvH",
                        TokenName = "TST3",
                        Decimals = 18
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x5da5cfe7d4ce1cc0712ebc0bb58eff93817a6801",
                        SRC20Address = "tQhh41Z6LCbiJEF78c2myQHqvbptsfPmSu",
                        TokenName = "TST4",
                        Decimals = 18
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x14f768657135d3daafb45d242157055f1c9143f3",
                        SRC20Address = "tJTNQT4inhr9PPZ7RF8B2tX5RaN1AdK2cp",
                        TokenName = "TST5",
                        Decimals = 18
                    }
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
                        SRC20Address = "CSnCmGCh8W8r2FeVFWtztXFXD8wbHH9Rwg",
                        TokenName = "WETH",
                        Decimals = 18
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x2260FAC5E5542a773Aa44fBCfeDf7C193bc2C599",
                        SRC20Address = "Ce79acmz7X5EXm8j4UUAw8F2EUhWu7wFea",
                        TokenName = "WBTC",
                        Decimals = 8
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48",
                        SRC20Address = "CKhmQHhqzdy4jRuFgh6ariMfnCB3fj2uwv",
                        TokenName = "USDC",
                        Decimals = 6
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0xdAC17F958D2ee523a2206206994597C13D831ec7",
                        SRC20Address = "CNwcowLp63MX5iZVh22LWtLMcFCeCj8FaU",
                        TokenName = "USDT",
                        Decimals = 6
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x514910771AF9Ca656af840dff83E8264EcF986CA",
                        SRC20Address = "CccwbTXHsi5sXmRSH6MeMK3xFbrzFp886p",
                        TokenName = "LINK",
                        Decimals = 18
                    },
                    new SupportedContractAddress()
                    {
                        NativeNetwork = SupportedNativeChain.Ethereum,
                        NativeNetworkAddress = "0x95aD61b0a150d79219dCF64E1E6Cc01f0B64C4cE",
                        SRC20Address = "CahVhkwht2PnLbf8iWEbwUSaCk7UrXrWFF",
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
