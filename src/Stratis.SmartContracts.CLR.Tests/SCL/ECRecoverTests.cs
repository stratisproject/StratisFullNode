using System;
using System.Linq;
using NBitcoin;
using Xunit;
using ECRecover = Stratis.SCL.Crypto.ECRecover;
using Operations = Stratis.SCL.Base.Operations;

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
            byte[][] sigs = keys.Select(k => Convert.FromBase64String(k.SignMessage(authorizationChallenge))).ToArray();
            byte[] flatSigs = Operations.FlattenArray(sigs, sigs[0].Length);

            Assert.True(ECRecover.TryVerifySignatures(flatSigs, System.Text.Encoding.ASCII.GetBytes(authorizationChallenge), addresses, out Address[] verifiedAddresses));
            Assert.Equal(addresses, verifiedAddresses);
        }
    }
}
