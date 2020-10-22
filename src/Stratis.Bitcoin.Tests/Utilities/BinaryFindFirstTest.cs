using System;
using System.Linq;
using Stratis.Bitcoin.Utilities;
using Xunit;

namespace Stratis.Bitcoin.Tests.Utilities
{
    public class BinaryFindFirstTest
    {
        [Fact]
        public void BinaryFindFirstFindsTheExpectedItem()
        {
            const int numItems = 1000000;
            const int numTests = 10000;

            int?[] testArray = Enumerable.Range(0, numItems).Select(n => (int?)n).ToArray();
            var rand = new Random(0);
            for (int i = 0; i < 100; i++)
            {
                testArray[rand.Next(numItems)] = null;
            }

            for (int i = 0; i < numTests; i++)
            {
                int itemToFind = rand.Next(numItems);
                testArray[itemToFind] = itemToFind;
                Assert.Equal(itemToFind, BinarySearch.BinaryFindFirst<int?>(testArray, x => (x == null) ? (bool?)null : (x >= itemToFind)));
            }
        }
    }
}
