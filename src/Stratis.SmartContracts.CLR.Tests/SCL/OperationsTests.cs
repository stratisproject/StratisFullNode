using Stratis.SCL.Base;
using Xunit;

namespace Stratis.SmartContracts.CLR.Tests.SCL
{
    public class OperationsTests
    {
        [Fact]
        public void CanUnflattenArray()
        {
            byte[][] res = Operations.UnflattenArray(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9}, 3);
            
            Assert.NotNull(res);
            Assert.Equal(3, res.Length);
            Assert.Equal(new byte[] { 1, 2, 3 }, res[0]);
            Assert.Equal(new byte[] { 4, 5, 6 }, res[1]);
            Assert.Equal(new byte[] { 7, 8, 9 }, res[2]);
        }
    }
}
