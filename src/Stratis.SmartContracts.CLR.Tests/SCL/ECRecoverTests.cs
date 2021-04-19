using System.Linq;
using NBitcoin;
using Xunit;
using ECRecover = Stratis.SCL.Crypto.ECRecover;

namespace Stratis.SmartContracts.CLR.Tests.SCL
{
    public class ECRecoverTests
    {
        [Fact]
        public void CanVerifySignatures()
        {
            string authorizationChallenge = "This is a test";
            Key[] keys = new[] { new Key(), new Key(), new Key() };
            Address[] addresses = keys.Select(k => k.PubKey.Hash.ToBytes().ToAddress()).ToArray();
            string[] sigs = keys.Select(k => k.SignMessage(authorizationChallenge)).ToArray();

            Assert.True(ECRecover.TryGetVerifiedSignatures(sigs, authorizationChallenge, addresses, out Address[] verifiedAddresses));
            Assert.Equal(addresses, verifiedAddresses);
        }
    }
}
