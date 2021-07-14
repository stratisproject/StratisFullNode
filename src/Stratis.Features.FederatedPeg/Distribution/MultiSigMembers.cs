using System.Collections.Generic;
using System.Linq;
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

        public static List<PubKey> RetrieveApplicableMultiSigMember(Network network)
        {
            if (network.IsTest())
                return InteropMultisigContractPubKeysTestNet;
            else if (network.IsRegTest())
                return Enumerable.Empty<PubKey>().ToList();
            else
                return InteropMultisigContractPubKeysMainNet;
        }

        /// <summary>
        /// This is the current set of multisig members that are participating in the multisig contract.
        /// </summary>
        /// <remarks>TODO: Refactor to make this list dynamic.</remarks>
        private static readonly List<PubKey> InteropMultisigContractPubKeysMainNet = new List<PubKey>()
        {
            new PubKey("03a37019d2e010b046ef9d0459e4844a015758007602ddfbdc9702534924a23695"),
            new PubKey("027e793fbf4f6d07de15b0aa8355f88759b8bdf92a9ffb8a65a87fa8ee03baeccd"),
            new PubKey("03e8809be396745434ee8c875089e518a3eef40e31ade81869ce9cbef63484996d"),
            new PubKey("03535a285d0919a9bd71df3b274cecb46e16b78bf50d3bf8b0a3b41028cf8a842d"),
            new PubKey("0317abe6a28cc7af44a46de97e7c6120c1ccec78afb83efe18030f5c36e3016b32"),
            new PubKey("03eb5db0b1703ea7418f0ad20582bf8de0b4105887d232c7724f43f19f14862488"),
        };

        /// <summary>
        /// This is the current set of multisig members that are participating in the multisig contract.
        /// </summary>
        /// <remarks>TODO: Refactor to make this list dynamic.</remarks>
        private static readonly List<PubKey> InteropMultisigContractPubKeysTestNet = new List<PubKey>()
        {
            new PubKey("021040ef28c82fcffb63028e69081605ed4712910c8384d5115c9ffeacd9dbcae4"), // Cirrus 1
            new PubKey("032df4a2d62c0db12cd1d66201819a10788637c9b90a1cd2a5a3f5196fdab7a621"), // Cirrus 3
            new PubKey("028ed190eb4ed6e46440ac6af21d8a67a537bd1bd7edb9cc5177d36d5a0972244d"), // Cirrus 4
        };
    }
}
