using NBitcoin;
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
            Network[] networks = new[] { KnownNetworks.StraxMain, KnownNetworks.StraxTest, KnownNetworks.StraxRegTest };
            string[] expectedAddresses = new[] { "yRL7Jn1Ytgc6g2k5Sidh6K4vf7cKTj6n45", "tD8CqzmXqjcTdX5G2y9WNMzbR5qUqiMFda", "tJDrisWrCAjBzR1w6SaogPrfMJBjoEom1v" };

            for (int i = 0; i < networks.Length; i++)
            {
                string address = networks[i].Federations.GetOnlyFederation().MultisigScript.PaymentScript.GetDestinationAddress(networks[i]).ToString();

                Assert.Equal(expectedAddresses[i], address);
            }
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
