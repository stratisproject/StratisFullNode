using System.Text;
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

            string encoded = InterFluxOpReturnEncoder.Encode((DestinationChain)chain, address);

            bool result = InterFluxOpReturnEncoder.TryDecode(encoded, out int resultChain, out string resultAddress);

            Assert.True(result);
            Assert.Equal(chain, resultChain);
            Assert.Equal(address, resultAddress);


            byte[] encodedBytes = Encoding.UTF8.GetBytes(encoded);
            result = InterFluxOpReturnEncoder.TryDecode(encodedBytes, out int resultChain2, out string resultAddress2);

            Assert.True(result);
            Assert.Equal(chain, resultChain2);
            Assert.Equal(address, resultAddress2);
        }

        [Fact]
        public void EncodeAndDecodeETHAddress()
        {
            string address = "0xd2390da742872294BE05dc7359D7249d7C79460E";
            string encoded = InterFluxOpReturnEncoder.Encode(DestinationChain.ETH, address);
            bool result = InterFluxOpReturnEncoder.TryDecode(encoded, out int resultChain, out string resultAddress);

            Assert.True(result);
            Assert.Equal(DestinationChain.ETH, (DestinationChain)resultChain);
            Assert.Equal(address, resultAddress);
        }

        [Fact]
        public void EncodeAndDecodeETHAddressLegacy_Pass()
        {
            bool result = InterFluxOpReturnEncoder.TryDecode("0xd2390da742872294BE05dc7359D7249d7C79460E", out int resultChain, out string resultAddress);

            Assert.True(result);
            Assert.Equal(DestinationChain.ETH, (DestinationChain)resultChain);
            Assert.Equal("0xd2390da742872294BE05dc7359D7249d7C79460E", resultAddress);
        }

        [Fact]
        public void EncodeAndDecodeETHAddressLegacy_Fail()
        {
            bool result = InterFluxOpReturnEncoder.TryDecode("0xd2390da742872294BE05dc7359D7249d7C9460E", out int resultChain, out string resultAddress);
            Assert.False(result);
        }

        [Fact]
        public void DecodeDoesntThrowWhenFormatIsIncorrect()
        {
            Assert.False(InterFluxOpReturnEncoder.TryDecode("INTER_3_345345", out int _, out string _));
            Assert.False(InterFluxOpReturnEncoder.TryDecode("INTER3_", out int _, out string _));
            Assert.False(InterFluxOpReturnEncoder.TryDecode("INTERefsdvsdvdsvsdv", out int _, out string _));
            Assert.False(InterFluxOpReturnEncoder.TryDecode("xvev456545cwsdfFSXVB365", out int _, out string _));
            Assert.False(InterFluxOpReturnEncoder.TryDecode("INTER1_aaaa", out int _, out string _));
        }
    }
}