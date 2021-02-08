using System.Linq;
using NBitcoin;
using NBitcoin.DataEncoders;
using Stratis.Bitcoin.Networks;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    // TODO: These tests can be removed after successful STRAX go-live.
    public class VerifyMultisigAddresses
    {
        [Fact]
        public void CheckMultisigAddresses()
        {
            // If this test fails due to changes in the way addresses are determined then make corresponding changes
            // in the FederationSetup tool in the legacy Stratis (SBFN) code-base!!!
            var networks = new Network[] { new StraxMain(), new StraxTest(), new StraxRegTest() };
            string[] expectedFederationIds = new[] {
                "029376b01f49af1d09798c2e70198bf56c30e2dccd2f1e90442b1a7c7adc92d9b6",
                "0221db203e65aab442bd726e6da6d12b174c722281eb9c8a0c33b58451e4285f31",
                "0347f6ba6232037a68ce2b8ac988c07c071eee1e7edd0e6bb9b3dbda22772ad96a"
            };
            string[] expectedMultisigScripts = new[]
            {
                "029376b01f49af1d09798c2e70198bf56c30e2dccd2f1e90442b1a7c7adc92d9b6 OP_FEDERATION OP_CHECKMULTISIG",
                "0221db203e65aab442bd726e6da6d12b174c722281eb9c8a0c33b58451e4285f31 OP_FEDERATION OP_CHECKMULTISIG",
                "0347f6ba6232037a68ce2b8ac988c07c071eee1e7edd0e6bb9b3dbda22772ad96a OP_FEDERATION OP_CHECKMULTISIG"
            };
            string[] expectedPaymentScripts = new[]
            {
                "OP_HASH160 5497bbc5b53dec5d78a2843f28eee6c76adccc9c OP_EQUAL",
                "OP_HASH160 692978125e0f2e6e246ecc32b59f17fab6bc0f1d OP_EQUAL",
                "OP_HASH160 9244ef1a1a829e2e94a652e071cb22d600ed4c40 OP_EQUAL"
            };
            string[] expectedAddresses = new[] {
                "yU2jNwiac7XF8rQvSk2bgibmwsNLkkhsHV",
                "tGWegFbA6e6QKZP7Pe3g16kFVXMghbSfY8",
                "tLG1HR71iEDKbKkvB8sH3Gy6HLF8o4Pnim"
            };
            string[] federationIds = networks.Select(n => Encoders.Hex.EncodeData(n.Federations.GetOnlyFederation().Id.ToBytes())).ToArray();
            Script[] multisigScripts = federationIds.Select((id, n) => networks[n].Federations.GetOnlyFederation().MultisigScript).ToArray();
            string[] paymentScripts = federationIds.Select((id, n) => multisigScripts[n].PaymentScript.ToString()).ToArray();
            string[] addresses = federationIds.Select((id, n) => multisigScripts[n].PaymentScript.GetDestinationAddress(networks[n]).ToString()).ToArray();

            Assert.Equal(expectedFederationIds[0], federationIds[0]);
            Assert.Equal(expectedFederationIds[1], federationIds[1]);
            Assert.Equal(expectedFederationIds[2], federationIds[2]);

            Assert.Equal(expectedMultisigScripts[0], multisigScripts[0].ToString());
            Assert.Equal(expectedMultisigScripts[1], multisigScripts[1].ToString());
            Assert.Equal(expectedMultisigScripts[2], multisigScripts[2].ToString());

            Assert.Equal(expectedPaymentScripts[0], paymentScripts[0]);
            Assert.Equal(expectedPaymentScripts[1], paymentScripts[1]);
            Assert.Equal(expectedPaymentScripts[2], paymentScripts[2]);

            Assert.Equal(expectedAddresses[0], addresses[0]);
            Assert.Equal(expectedAddresses[1], addresses[1]);
            Assert.Equal(expectedAddresses[2], addresses[2]);
        }

        [Fact]
        public void CheckBech32Prefixes()
        {
            // If you change these constants then you will also need to change it in the RecoveryTransactionCreator class 
            // in the FederationSetup tool in the legacy Stratis (SBFN) code-base!!!
            const byte straxMainScriptAddressPrefix = 140;
            const byte straxTestScriptAddressPrefix = 127;
            const byte straxRegTestScriptAddressPrefix = 127;

            Assert.Single(KnownNetworks.StraxMain.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS]);
            Assert.Equal(straxMainScriptAddressPrefix, KnownNetworks.StraxMain.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS][0]);
            Assert.Single(KnownNetworks.StraxTest.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS]);
            Assert.Equal(straxTestScriptAddressPrefix, KnownNetworks.StraxTest.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS][0]);
            Assert.Single(KnownNetworks.StraxRegTest.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS]);
            Assert.Equal(straxRegTestScriptAddressPrefix, KnownNetworks.StraxRegTest.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS][0]);
        }
    }
}
