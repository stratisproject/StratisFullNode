using Xunit;

namespace Stratis.Bitcoin.Features.Wallet.Tests
{
    public class InterFluxOpReturnEncoderTest
    {
        [Fact]
        public void CanEncodeAndDecode()
        {
            int chain = 3;
            string address = "0x51EC92A3aB8cfcA412Ea43766A9259523fC81501";

            string encoded = InterFluxOpReturnEncoder.Encode(chain, address);

            bool success = InterFluxOpReturnEncoder.TryDecode(encoded, out int resultChain, out string resultAddress);

            Assert.True(success);
            Assert.Equal(chain, resultChain);
            Assert.Equal(address, resultAddress);
        }

        [Fact]
        public void DecodeDoesntThrowWhenFormatIsIncorrect()
        {
            Assert.False(InterFluxOpReturnEncoder.TryDecode("INTER_3_345345", out int _, out string _));
            Assert.False(InterFluxOpReturnEncoder.TryDecode("INTER3_", out int _, out string _));
            Assert.False(InterFluxOpReturnEncoder.TryDecode("INTERefsdvsdvdsvsdv", out int _, out string _));
            Assert.False(InterFluxOpReturnEncoder.TryDecode("xvev456545cwsdfFSXVB365", out int _, out string _));
        }
    }
}
