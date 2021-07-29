﻿using System.Text;
using System.Text.RegularExpressions;

namespace Stratis.Bitcoin.Features.Wallet
{
    /// <summary>Encodes or decodes InterFlux related data to and from OP_RETURN data.</summary>
    public static class InterFluxOpReturnEncoder
    {
        private static string InterFluxPrefix = "INTER";

        public static string Encode(DestinationChain destinationChain, string address)
        {
            return InterFluxPrefix + (int)destinationChain + "_" + address;
        }

        public static bool TryDecode(string opReturnData, out int destinationChain, out string address)
        {
            int prefixIndex = opReturnData.IndexOf(InterFluxPrefix);
            int separatorIndex = opReturnData.IndexOf("_");

            destinationChain = -1;
            address = string.Empty;

            // If the op_return data does not contain the "INTER" prefix,
            // then this is not a interflux (conversion) transaction.
            if (prefixIndex == -1 || separatorIndex == -1)
            {
                // If there is no interflux prefix then this could potentially be a legacy ETH conversion.
                // Conversion requests to ETH could be submitted from users on a previous versions of the wallet
                // which will utilize the older OP_RETURN format (without the destination chain).
                if (TryConvertValidOpReturnDataToETHAddress(opReturnData))
                {
                    address = opReturnData;
                    destinationChain = (int)DestinationChain.ETH;
                    return true;
                }

                return false;
            }

            // Try and extract the destination chain.
            if (!int.TryParse(opReturnData.Substring(InterFluxPrefix.Length, separatorIndex - InterFluxPrefix.Length), out destinationChain))
                return false;

            address = opReturnData.Substring(separatorIndex + 1);

            // If the destination chain is Ethereum, try and validate.
            // Once conversions to other chains are implemented, we can add validation here.
            if (destinationChain == (int)DestinationChain.ETH)
            {
                if (!TryConvertValidOpReturnDataToETHAddress(address))
                    return false;
            }

            return !string.IsNullOrEmpty(address);
        }

        /// <summary>
        /// Try and validate an address on the Ethereum chain.
        /// </summary>
        /// <param name="potentialETHAddress">The address to validate.</param>
        /// <returns><c>true</c> if valid, <c>false</c> otherwise.</returns>
        private static bool TryConvertValidOpReturnDataToETHAddress(string potentialETHAddress)
        {
            // Attempt to parse the string. An Ethereum address is 42 characters:
            // 0x - initial prefix
            // <20 bytes> - rightmost 20 bytes of the Keccak hash of a public key, encoded as hex
            Match match = Regex.Match(potentialETHAddress, @"^0x([A-Fa-f0-9]{40})$", RegexOptions.IgnoreCase);
            return match.Success;
        }

        public static bool TryDecode(byte[] opReturnData, out int destinationChain, out string address)
        {
            string stringData = Encoding.UTF8.GetString(opReturnData);

            return TryDecode(stringData, out destinationChain, out address);
        }
    }

    /// <summary>Chains supported by InterFlux integration.</summary>
    public enum DestinationChain
    {
        STRAX = 0, // Stratis
        ETH = 1, // Ethereum
        BNB = 2, // Binance

        ETC = 3, // Ethereum classic
        AVAX = 4, // Avalanche
        ADA = 5, // Cardano
    }
}