using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace Stratis.SCL.Crypto
{
    public static class SSAS
    {
        class ChameleonNetwork : Network
        {
            public ChameleonNetwork(byte base58Prefix)
            {
                this.Base58Prefixes = new byte[][] { new byte[] { base58Prefix } };
            }
        }

        public static byte[] ParseAddress(string address, out byte prefix)
        {
            prefix = (new Base58Encoder()).DecodeData(address)[0];
            var bitcoinAddress = BitcoinAddress.Create(address, new ChameleonNetwork(prefix));
            var pubKeyHash = ((BitcoinPubKeyAddress)bitcoinAddress).Hash;

            return pubKeyHash.ToBytes();
        }

        public static string[] GetURLArguments(string url, string[] argumentNames)
        {
            // Create a mapping of available url arguments.
            Dictionary<string, string> argDict = ParseQueryString(url);

            return argumentNames.Select(argName => argDict[argName]).ToArray();
        }

        private static Dictionary<string, string> ParseQueryString(string queryString)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();

            int startOfQueryString = queryString.IndexOf('?') + 1;

            if (!string.IsNullOrEmpty(queryString) && startOfQueryString != 0)
            {
                // Remove the '?' at the start of the query string
                queryString = queryString.Substring(startOfQueryString);

                foreach (var part in queryString.Split('&'))
                {
                    var keyValue = part.Split('=');

                    if (keyValue.Length == 2)
                    {
                        result[keyValue[0]] = Uri.UnescapeDataString(keyValue[1]);
                    }
                }
            }

            return result;
        }
    }
}
