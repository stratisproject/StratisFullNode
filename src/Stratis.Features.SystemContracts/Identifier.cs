using System;
using System.Linq;
using NBitcoin;

namespace Stratis.Features.SystemContracts
{
    public struct Identifier
    {
        private const int Uint256Width = 32;
        private const int Uint160Width = 20;

        public Identifier(uint160 data)
        {
            this.Data = data;
        }

        public uint160 Data { get; }

        public uint256 Padded()
        {            
            return new uint256(LeftPad(this.Data.ToBytes(), Uint256Width));
        }

        public static byte[] LeftPad(byte[] input, int len)
        {
            if (input.Length > len)
                return null;

            var bytes = input.ToArray();
            var result = new byte[len];
            var start = len - bytes.Length;

            Buffer.BlockCopy(bytes, 0, result, start, bytes.Length);

            return result;
        }

        public byte[] ToBytes()
        {
            return this.Data.ToBytes();
        }
    }
}
