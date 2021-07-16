using System.Collections.Generic;
using NBitcoin;
using Stratis.Bitcoin.Utilities;

namespace Stratis.Features.FederatedPeg.Distribution
{
    public static class MultiSigMembers
    {
        public static bool IsContractOwner(Network network, PubKey pubKey)
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
            new PubKey("027e793fbf4f6d07de15b0aa8355f88759b8bdf92a9ffb8a65a87fa8ee03baeccd"),//
            new PubKey("03e8809be396745434ee8c875089e518a3eef40e31ade81869ce9cbef63484996d"),//
            new PubKey("02f40bd4f662ba20629a104115f0ac9ee5eab695716edfe01b240abf56e05797e2"),//
            new PubKey("03535a285d0919a9bd71df3b274cecb46e16b78bf50d3bf8b0a3b41028cf8a842d"),//
            new PubKey("0317abe6a28cc7af44a46de97e7c6120c1ccec78afb83efe18030f5c36e3016b32"),//
            new PubKey("03eb5db0b1703ea7418f0ad20582bf8de0b4105887d232c7724f43f19f14862488"),//
            new PubKey("03d8b5580b7ec709c006ef497327db27ea323bd358ca45412171c644214483b74f"),//
            new PubKey("0323033679aa439a0388f09f2883bf1ca6f50283b41bfeb6be6ddcc4e420144c16"),//
            new PubKey("025cb67811d0922ca77fa33f19c3e5c37961f9639a1f0a116011b9075f6796abcb"),//
            new PubKey("028e1d9fd64b84a2ec85fac7185deb2c87cc0dd97270cf2d8adc3aa766dde975a7"),//
            new PubKey("036437789fac0ab74cda93d98b519c28608a48ef86c3bd5e8227af606c1e025f61"),//
            new PubKey("03f5de5176e29e1e7d518ae76c1e020b1da18b57a3713ac81b16015026e232748e"),//
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
