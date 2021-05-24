using Xunit;

namespace Stratis.Patricia.Tests
{
    public class ByteArrayComparerTests
    {
        [Fact]
        public void Test()
        {
            var bytes = new byte[]
            {
                1,
                2,
                5,
                234
            };

            var bytes2 = new byte[]
            {
                1,
                2,
                5,
                234
            };

            var bytes3 = new byte[]
            {
                1,
                2,
                5,
                235
            };

            Assert.True(new ByteArrayComparer().Equals(bytes, bytes2));
            Assert.False(new ByteArrayComparer().Equals(bytes, bytes3));
        }
    }
}
