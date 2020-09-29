using System;
using System.Collections.Generic;

namespace Stratis.Bitcoin.Utilities
{
    /// <summary>
    /// Compares two byte arrays for equality.
    /// </summary>
    public sealed class ByteArrayComparer : IEqualityComparer<byte[]>, IComparer<byte[]>
    {
        public int Compare(byte[] first, byte[] second)
        {
            int firstLen = first?.Length ?? -1;
            int secondLen = second?.Length ?? -1;
            int commonLen = Math.Min(firstLen, secondLen);

            for (int i = 0; i < commonLen; i++)
            {
                if (first[i] == second[i])
                    continue;

                return (first[i] < second[i]) ? -1 : 1;
            }

            return firstLen.CompareTo(secondLen);
        }

        public bool Equals(byte[] first, byte[] second)
        {
            return this.Compare(first, second) == 0;
        }

        public int GetHashCode(byte[] obj)
        {
            ulong hash = 17;

            foreach (byte objByte in obj)
            {
                hash = (hash << 5) - hash + objByte;
            }

            return (int)hash;
        }
    }
}
