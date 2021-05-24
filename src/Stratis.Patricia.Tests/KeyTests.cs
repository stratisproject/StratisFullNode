using Xunit;

namespace Stratis.Patricia.Tests
{
    public class KeyTests
    {
        [Fact]
        public void TestEmptyKey()
        {
            var testKey = Key.Empty(true);
            Assert.Equal(0, testKey.Length);
            Assert.True(testKey.IsTerminal);
            Assert.True(testKey.IsEmpty);
        }
    }
}
