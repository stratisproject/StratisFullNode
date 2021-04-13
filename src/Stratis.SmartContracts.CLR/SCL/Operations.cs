using System;
using Stratis.SmartContracts;

namespace Stratis.SCL.Base
{
    public static class Operations
    {
        public static Address DeserializeAddress(byte[] address)
        {
            var pn = new uint[5];
            for (int i = 0, offs = 0; i < pn.Length; i++, offs += 4)
                pn[i] = BitConverter.ToUInt32(address, offs);
            return new Address(pn[0], pn[1], pn[2], pn[3], pn[4]);
        }

        public static byte[][] DeserializeSignatures(byte[] signatures)
        {
            const int signatureLength = 65;
            const int minHeaderByte = 27;
            const int maxHeaderByte = 34;
            int cnt = signatures.Length / signatureLength;

            if (signatures.Length != signatureLength * cnt)
                return null;

            var buffer = new byte[cnt][];
            for (int i = 0; i < cnt; i++)
            {
                Array.Copy(signatures, i * signatureLength, buffer[i], 0, signatureLength);
                if (buffer[i][0] < minHeaderByte || buffer[i][0] > maxHeaderByte)
                    return null;
            }

            return buffer;
        }
    }
}
