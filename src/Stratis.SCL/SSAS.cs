using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.SmartContracts;

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

            return argumentNames.Select(argName => argDict.TryGetValue(argName, out string argValue) ? argValue : null).ToArray();
        }

        /// <summary>
        /// Retrieves the address of the signer of an ECDSA signature.
        /// </summary>
        /// <param name="message">The message that was signed.</param>
        /// <param name="signature">The ECDSA signature prepended with header information specifying the correct value of recId.</param>
        /// <param name="address">The Address for the signer of a signature.</param>
        /// <returns>A bool representing whether or not the signer was retrieved successfully.</returns>
        public static bool TryGetSignerSHA256(byte[] message, byte[] signature, out Address address)
        {
            address = Address.Zero;

            if (message == null || signature == null)
                return false;

            // NBitcoin is very throwy
            try
            {
                PubKey pubKey = PubKey.RecoverFromMessage(message, Convert.ToBase64String(signature));

                address = CreateAddress(pubKey.Hash.ToBytes());

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Address CreateAddress(byte[] bytes)
        {
            uint pn0 = BitConverter.ToUInt32(bytes, 0);
            uint pn1 = BitConverter.ToUInt32(bytes, 4);
            uint pn2 = BitConverter.ToUInt32(bytes, 8);
            uint pn3 = BitConverter.ToUInt32(bytes, 12);
            uint pn4 = BitConverter.ToUInt32(bytes, 16);

            return new Address(pn0, pn1, pn2, pn3, pn4);
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
