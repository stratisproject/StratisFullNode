using System.Linq;
using NBitcoin;
using Xunit;

namespace Stratis.Features.SystemContracts.Tests
{
    public class IdentifierTests
    {
        [Fact]
        public void Identifiers_Are_Equal()
        {
            Assert.Equal(new Identifier(uint160.One), new Identifier(uint160.One));
        }

        [Fact]
        public void Identifiers_Are_Not_Equal()
        {
            Assert.NotEqual(new Identifier(uint160.One), new Identifier(uint160.Zero));
        }

        [Fact]
        public void Identifier_Is_Padded()
        {
            var identifier = new Identifier(uint160.One);

            var padded = identifier.Padded();

            var bytes = padded.ToBytes();
            
            var diff = bytes.Length - identifier.ToBytes().Length;

            // Check the padding
            bytes.Take(diff).ToList().ForEach(item => Assert.Equal(0, item));

            // Check the rest
            Assert.True(bytes.Skip(diff).ToList().SequenceEqual(identifier.ToBytes()));
        }
    }
}
