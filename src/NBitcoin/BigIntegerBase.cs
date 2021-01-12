using System;
using System.Numerics;
using NBitcoin.DataEncoders;
using System.Linq;

namespace NBitcoin
{
    public class BigIntegerBase : IComparable
    {
        private static readonly HexEncoder Encoder = new HexEncoder();

        protected BigInteger value;
        protected int width;

        public BigIntegerBase(BigIntegerBase value) : this(value.width, value.value)
        {
        }

        public BigIntegerBase(int width)
        {
            if ((width & 3) != 0)
                throw new ArgumentException($"The '{nameof(width)}' must be a multiple of 4.");
            this.width = width;
        }

        public BigIntegerBase(int width, ulong b) : this(width)
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

        private bool TooBig(byte[] bytes)
        {
            if (bytes.Length <= this.width)
                return false;
            if (bytes.Length == (this.width + 1) && bytes[this.width] == 0)
                return false;
            return true;
        }

        private void SetValue(BigInteger newValue)
        {
            if (TooBig(newValue.ToByteArray()))
                throw new OverflowException();

            this.value = newValue;
        }

        public uint[] ToUIntArray()
        {
            var bytes = this.ToBytes();
            int length = this.width / 4;
            uint[] pn = new uint[length];
            for (int i = 0; i < length; i++)
                pn[i] = BitConverter.ToUInt32(bytes, i * 4);

            return pn;
        }

        public BigIntegerBase(int width, string hex) : this(width, HexBytes(hex))
        {
        }

        public BigIntegerBase(int width, BigIntegerBase value) : this(width, value.value)
        {
        }

        public BigIntegerBase(int width, BigInteger value) : this(width)
        {
            SetValue(value);
        }

        public BigIntegerBase(int width, byte[] vch, bool lendian = true) : this(width)
        {
            if (vch.Length != this.width)
                throw new FormatException($"The byte array should be {this.width} bytes long.");

            if (!lendian)
                vch = vch.Reverse().ToArray();

            SetValue(new BigInteger(vch, true));
        }

        public BigIntegerBase(int width, uint[] array) : this(width)
        {
            int length = this.width / 4;

            if (array.Length != length)
                throw new FormatException($"The array length should be {length}.");

            byte[] bytes = new byte[this.width];

            for (int i = 0; i < length; i++)
                BitConverter.GetBytes(array[i]).CopyTo(bytes, i * 4);

            SetValue(new BigInteger(bytes, true));
        }

        public byte[] ToBytes(bool lendian = true)
        {
            var arr1 = this.value.ToByteArray();
            var arr = new byte[this.width];
            Array.Copy(arr1, arr, Math.Min(arr1.Length, arr.Length));

            if (!lendian)
                Array.Reverse(arr);

            return arr;
        }

        protected BigInteger ShiftRight(int shift) => this.value >> shift;

        protected BigInteger ShiftLeft(int shift) => this.value << shift;        

        protected BigInteger Add(BigInteger value2) => this.value + value2;

        protected BigInteger Subtract(BigInteger value2)
        {
            if (this.value.CompareTo(value2) < 0)
                throw new ArithmeticException("Number cannot be negative.");

            return this.value - value2;
        }

        protected BigInteger Multiply(BigInteger value2) => this.value * value2;

        protected BigInteger Divide(BigInteger value2) => this.value / value2;

        protected BigInteger Mod(BigInteger value2) => this.value % value2;

        public int CompareTo(object b)
        {
            return this.value.CompareTo(((BigIntegerBase)b).value);
        }

        public static int Comparison(BigIntegerBase a, BigIntegerBase b)
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

        public byte GetByte(int n)
        {
            if (n >= this.width)
                throw new ArgumentOutOfRangeException();

            return this.ToBytes()[n];
        }

        public uint GetLow32()
        {
            var pn = ToUIntArray();
            return pn[0];
        }

        public ulong GetULong(int position)
        {
            var pn = ToUIntArray();
            int length = this.width / 8;

            if (position >= length)
                throw new ArgumentOutOfRangeException($"Argument '{nameof(position)}' should be less than {length}.", nameof(position));

            return (ulong)pn[position * 2] + (ulong)((ulong)pn[position * 2 + 1] << 32);
        }
    }

    public class BigInteger256 : BigIntegerBase
    {
        const int WIDTH = 32;

        public BigInteger256() : base(WIDTH)
        {
        }

        public BigInteger256(ulong b) : base(WIDTH, b)
        {
        }

        public BigInteger256(string hex) : base(WIDTH, hex)
        {
        }

        public BigInteger256(BigInteger value) : base(WIDTH, value)
        {
        }

        public BigInteger256(byte[] vch, bool lendian = true) : base(WIDTH, vch, lendian)
        {
        }

        public BigInteger256(uint[] array) : base(WIDTH, array)
        {
        }
    }

    public class BigInteger160 : BigIntegerBase
    {
        const int WIDTH = 20;

        public BigInteger160() : base(WIDTH)
        {
        }

        public BigInteger160(ulong b) : base(WIDTH, b)
        {
        }

        public BigInteger160(string hex) : base(WIDTH, hex)
        {
        }

        public BigInteger160(BigInteger value) : base(WIDTH, value)
        {
        }

        public BigInteger160(byte[] vch, bool lendian = true) : base(WIDTH, vch, lendian)
        {
        }

        public BigInteger160(uint[] array) : base(WIDTH, array)
        {
        }
    }

    public class BigInteger128 : BigIntegerBase
    {
        const int WIDTH = 16;

        public BigInteger128() : base(WIDTH)
        {
        }

        public BigInteger128(ulong b) : base(WIDTH, b)
        {
        }

        public BigInteger128(string hex) : base(WIDTH, hex)
        {
        }

        public BigInteger128(BigInteger value) : base(WIDTH, value)
        {
        }

        public BigInteger128(byte[] vch, bool lendian = true) : base(WIDTH, vch, lendian)
        {
        }

        public BigInteger128(uint[] array) : base(WIDTH, array)
        {
        }
    }
}
