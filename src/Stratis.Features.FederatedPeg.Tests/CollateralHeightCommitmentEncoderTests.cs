using System;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.Bitcoin.Tests.Common;
using Stratis.Features.PoA.Collateral;
using Xunit;

namespace Stratis.Features.FederatedPeg.Tests
{
    public sealed class CollateralHeightCommitmentEncoderTests
    {
        private readonly CollateralHeightCommitmentEncoder encoder;

        private readonly Random r;

        public CollateralHeightCommitmentEncoderTests()
        {
            ILogger logger = new Mock<ILogger>().Object;
            this.encoder = new CollateralHeightCommitmentEncoder();
            this.r = new Random();
        }

        [Fact]
        public void CanEncodeAndDecode()
        {
            for (int i = 0; i < 1000; i++)
            {
                int randomValue = this.r.Next();

                byte[] encodedWithPrefix = this.encoder.EncodeCommitmentHeight(randomValue);

                var votingOutputScript = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(encodedWithPrefix), Op.GetPushOp(KnownNetworks.StraxMain.MagicBytes));
                var tx = new Transaction();
                tx.AddOutput(Money.Zero, votingOutputScript);

                (int? decodedValue, uint? magic) = this.encoder.DecodeCommitmentHeight(tx);

                Assert.Equal(randomValue, decodedValue);
                Assert.Equal(KnownNetworks.StraxMain.Magic, magic);
            }
        }
    }
}
