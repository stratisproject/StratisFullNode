using System;

namespace Stratis.SCL.Base
{
    public static class Operations
    {
        public static byte[][] DeflattenByteArray(byte[] array, int subArrayLength)
        {
            int cnt = array.Length / subArrayLength;

            if (array.Length != subArrayLength * cnt)
                return null;

            var buffer = new byte[cnt][];
            for (int i = 0; i < cnt; i++)
                Array.Copy(array, i * subArrayLength, buffer[i], 0, subArrayLength);

            return buffer;
        }
    }
}
