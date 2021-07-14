using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.FederatedPeg.Distribution
{
    internal static class MultiSigMembers
    {
        internal static bool IsContractOwner(Network network, PubKey pubKey)
        {
            if (network.IsTest())
                return InteropMultisigContractPubKeysTestNet.Contains(pubKey);
            else if (network.IsRegTest())
                return true;
            else
                return InteropMultisigContractPubKeysMainNet.Contains(pubKey);
        }

        /// <summary>
        /// This is the current set of multisig members that are participating in the multisig contract.
        /// </summary>
        /// <remarks>TODO: Refactor to make this list dynamic.</remarks>
        private static readonly List<PubKey> InteropMultisigContractPubKeysMainNet = new List<PubKey>()
        {
            // To add
        };

        /// <summary>
        /// This is the current set of multisig members that are participating in the multisig contract.
        /// </summary>
        /// <remarks>TODO: Refactor to make this list dynamic.</remarks>
        private static readonly List<PubKey> InteropMultisigContractPubKeysTestNet = new List<PubKey>()
        {
            new PubKey("03cfc06ef56352038e1169deb3b4fa228356e2a54255cf77c271556d2e2607c28c"), // Cirrus 1
            new PubKey("02fc828e06041ae803ab5378b5ec4e0def3d4e331977a69e1b6ef694d67f5c9c13"), // Cirrus 3
            new PubKey("02fd4f3197c40d41f9f5478d55844f522744258ca4093b5119571de1a5df1bc653"), // Cirrus 4
        };
    }
}
