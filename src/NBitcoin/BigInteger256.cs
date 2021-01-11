using System;
using System.Numerics;
using NBitcoin.DataEncoders;
using System.Linq;

namespace NBitcoin
{
    public class BigInteger256 : IComparable
    {
        private static readonly HexEncoder Encoder = new HexEncoder();

        BigInteger value;

        public BigInteger256()
        {
        }

        public BigInteger256(ulong b) : this()
        {
            SetValue(new BigInteger(b));
        }

        private static byte[] HexBytes(string str)
        {
            str = str.Trim();

            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                str = str.Substring(2);

            return Encoder.DecodeData(str).Reverse().ToArray();
        }

        private static bool TooBig(BigInteger newValue)
        {
            var bytes = newValue.ToByteArray();
            if (bytes.Length <= 32)
                return false;
            if (bytes.Length == 33 && bytes[32] == 0)
                return false;
            return true;
        }

        private void SetValue(BigInteger newValue)
        {
            if (TooBig(newValue))
                throw new OverflowException();

            this.value = newValue;
        }

        public uint[] ToUIntArray()
        {
            var bytes = this.ToBytes();
            uint[] pn = new uint[8];
            for (int i = 0; i < 8; i++)
                pn[i] = BitConverter.ToUInt32(bytes, i * 4);

            return pn;
        }

        public BigInteger256(string hex) : this(HexBytes(hex))
        {
        }

        public BigInteger256(BigInteger256 value) : this(value.value)
        {
        }

        public BigInteger256(BigInteger value)
        {
            SetValue(value);
        }

        public BigInteger256(byte[] vch, bool lendian = true)
        {
            if (vch.Length != 32)
                throw new FormatException($"The byte array should be 32 bytes long.");

            if (!lendian)
                vch = vch.Reverse().ToArray();

            SetValue(new BigInteger(vch, true));
        }

        public BigInteger256(uint[] array)
        {
            if (array.Length != 8)
                throw new FormatException($"The array length should be 8.");

            byte[] bytes = new byte[32];

            for (int i = 0; i < 8; i++)
                BitConverter.GetBytes(array[i]).CopyTo(bytes, i * 4);

            SetValue(new BigInteger(bytes));
        }

        public byte GetByte(int n)
        {
            return this.ToBytes()[n];
        }

        public byte[] ToBytes(bool lendian = true)
        {
            var arr1 = this.value.ToByteArray();
            var arr = new byte[32];
            Array.Copy(arr1, arr, Math.Min(arr1.Length, arr.Length));

            if (!lendian)
                Array.Reverse(arr);

            return arr;
        }

        protected static BigInteger256 ShiftRight(BigInteger256 source, int shift)
        {
            return new BigInteger256(source.value >> shift);
        }

        protected static BigInteger256 ShiftLeft(BigInteger256 source, int shift)
        {
            return new BigInteger256(source.value << shift);
        }

        protected static BigInteger256 Add(BigInteger256 value1, BigInteger256 value2)
        {
            return new BigInteger256(value1.value + value2.value);
        }

        protected static BigInteger256 Subtract(BigInteger256 value1, BigInteger256 value2)
        {
            if (value1.CompareTo(value2) < 0)
                throw new ArithmeticException("Number cannot be negative.");

            return new BigInteger256(value1.value - value2.value);
        }

        protected static BigInteger256 Multiply(BigInteger256 value1, BigInteger256 value2)
        {
            return new BigInteger256(value1.value * value2.value);
        }

        protected static BigInteger256 Divide(BigInteger256 value1, BigInteger256 value2)
        {
            return new BigInteger256(value1.value / value2.value);
        }

        protected static BigInteger256 Mod(BigInteger256 value1, BigInteger256 value2)
        {
            return new BigInteger256(value1.value % value2.value);
        }

        public int CompareTo(object b)
        {
            return this.value.CompareTo(((BigInteger256)b).value);
        }

        public static int Comparison(BigInteger256 a, BigInteger256 b)
        {
            return a.CompareTo(b);
        }

        public override int GetHashCode()
        {
            var pn = ToUIntArray();
            uint hash = 0;
            for (int i = 0; i < pn.Length; i++)
                hash ^= pn[i];
            return (int)hash;
        }

        public override bool Equals(object obj)
        {
            return this.CompareTo(obj) == 0;
        }

        public override string ToString()
        {
            return Encoder.EncodeData(ToBytes().Reverse().ToArray());
        }
    }
}
