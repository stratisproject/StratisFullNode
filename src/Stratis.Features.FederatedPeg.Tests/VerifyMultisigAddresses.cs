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
                "0272ef88df1ac2bc9b14b05ced742c2a9c2f76e48ccb92258136750d0ae2af3859",
                "022c7d264dd56381d51526365f8e439a68fb8b5cd0272e073ee4047999e0bb034f",
                "009b9de39718cf6042d336e1fff3676fd9a651376399efb5472b0b1733769341e1"
            };
            string[] expectedMultisigScripts = new[]
            {
                "0272ef88df1ac2bc9b14b05ced742c2a9c2f76e48ccb92258136750d0ae2af3859 OP_FEDERATION OP_CHECKMULTISIG",
                "022c7d264dd56381d51526365f8e439a68fb8b5cd0272e073ee4047999e0bb034f OP_FEDERATION OP_CHECKMULTISIG",
                "009b9de39718cf6042d336e1fff3676fd9a651376399efb5472b0b1733769341e1 OP_FEDERATION OP_CHECKMULTISIG"
            };
            string[] expectedPaymentScripts = new[]
            {
                "OP_HASH160 36f8bb703df2de2b4ddc0448dd28aac5d24a0e6d OP_EQUAL",
                "OP_HASH160 44026086e900f9f3bdd34ed2e9436ceef05c09b3 OP_EQUAL",
                "OP_HASH160 a379dfdfd8a6cb4b7fe522fa9d5a695cece3901a OP_EQUAL"
            };
            string[] expectedAddresses = new[] {
                "yRL7Jn1Ytgc6g2k5Sidh6K4vf7cKTj6n45",
                "tD8CqzmXqjcTdX5G2y9WNMzbR5qUqiMFda",
                "tMpzC3oa7ktseYpvRAihp4F9RJAYSB7WWB"
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
