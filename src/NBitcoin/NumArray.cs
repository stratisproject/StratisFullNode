using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NBitcoin.DataEncoders;

namespace NBitcoin
{
    public class NumConverter<T> where T : struct, IComparable
    {
        static Dictionary<Type, Func<byte[], object>> conversions = new Dictionary<Type, Func<byte[], object>>() {
            { typeof(byte), x => (byte)BitConverter.ToChar(x) },
            { typeof(ushort), x => BitConverter.ToUInt16(x) },
            { typeof(uint), x => BitConverter.ToUInt32(x) },
            { typeof(ulong), x => BitConverter.ToUInt64(x) },
        };

        public static T FromBytes(Type type, byte[] bytes)
        {
            return (T)conversions[type](bytes);
        }
    }

    public class NumArray<T> : IComparable where T : struct, IComparable
    {
        // Least significant first.
        protected T[] pn;

        private static int WORDSIZE = Marshal.SizeOf(new T());
        private static int BITS = 8 * WORDSIZE;
        private static readonly HexEncoder Encoder = new HexEncoder();

        private int WIDTH_BYTE => this.pn.Length * WORDSIZE;

        public NumArray(int length)
        {
            this.pn = new T[length];
        }

        public NumArray(NumArray<T> value) : this(value.pn)
        {
        }

        public NumArray(T[] pn)
        {
            this.pn = (T[])pn.Clone();
        }

        public NumArray(int length, byte[] vch, bool lendian = true) : this(length)
        {
            if (vch.Length != length * WORDSIZE)
                throw new FormatException($"The byte array should be { (length * WORDSIZE) } bytes long.");

            if (!lendian)
                vch = vch.Reverse().ToArray();

            using (MemoryStream ms = new MemoryStream(vch))
            {
                for (int i = 0; i < length; i++)
                    this.pn[i] = NumConverter<T>.FromBytes(typeof(T), ms.ReadBytes(WORDSIZE));
            }
        }

        public NumArray(int length, string str) : this(length, HexBytes(str), true)
        {
        }

        private static byte[] HexBytes(string str)
        {
            str = str.Trim();

            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                str = str.Substring(2);

            return Encoder.DecodeData(str).Reverse().ToArray();
        }

        protected T[] ToArray()
        {
            return (T[])this.pn.Clone();
        }

        protected static T[] ShiftLeft(T[] source, int shift)
        {
            var target = new T[source.Length];
            int k = shift / BITS;
            shift = shift % BITS;

            for (int i = 0; i < target.Length; i++)
            {
                if (i + k + 1 < target.Length && shift != 0)
                    target[i + k + 1] |= ((dynamic)source[i] >> (BITS - shift));
                if (i + k < target.Length)
                    target[i + k] |= ((dynamic)source[i] << shift);
            }

            return target;
        }

        public static NumArray<T> operator <<(NumArray<T> a, int shift)
        {
            return new NumArray<T>(ShiftLeft(a.ToArray(), shift));
        }

        protected static T[] ShiftRight(T[] source, int shift)
        {
            var target = new T[source.Length];
            int k = shift / BITS;
            shift = shift % BITS;
            for (int i = 0; i < target.Length; i++)
            {
                if (i - k - 1 >= 0 && shift != 0)
                    target[i - k - 1] |= ((dynamic)source[i] << (BITS - shift));
                if (i - k >= 0)
                    target[i - k] |= ((dynamic)source[i] >> shift);
            }

            return target;
        }

        public static NumArray<T> operator >>(NumArray<T> a, int shift)
        {
            return new NumArray<T>(ShiftRight(a.ToArray(), shift));
        }

        public int CompareTo(object obj)
        {
            var item = obj as NumArray<T>;
            if (item == null)
                return 1;

            for (int i = this.pn.Length - 1; i >= 0; i--)
            {
                int cmp = this.pn[i].CompareTo(item.pn[i]);
                if (cmp != 0)
                    return cmp;
            }

            return 0;
        }

        public override bool Equals(object obj)
        {
            return this.CompareTo(obj) == 0;
        }

        public static int Comparison(NumArray<T> a, NumArray<T> b)
        {
            if (a == null)
                throw new ArgumentNullException("a");
            if (b == null)
                throw new ArgumentNullException("b");

            return a.CompareTo(b);
        }

        public static bool operator ==(NumArray<T> a, NumArray<T> b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (((object)a == null) || ((object)b == null))
                return false;

            return a.Equals(b);
        }

        public static bool operator !=(NumArray<T> a, NumArray<T> b)
        {
            return !(a == b);
        }

        public static bool operator <(NumArray<T> a, NumArray<T> b)
        {
            return Comparison(a, b) < 0;
        }

        public static bool operator >(NumArray<T> a, NumArray<T> b)
        {
            return Comparison(a, b) > 0;
        }

        public static bool operator <=(NumArray<T> a, NumArray<T> b)
        {
            return Comparison(a, b) <= 0;
        }

        public static bool operator >=(NumArray<T> a, NumArray<T> b)
        {
            return Comparison(a, b) >= 0;
        }

        public byte GetByte(int index)
        {
            int uintIndex = index / WORDSIZE;
            if (uintIndex >= this.pn.Length)
                throw new ArgumentOutOfRangeException("index");

            return (byte)((dynamic)this.pn[uintIndex] >> ((index % WORDSIZE) * 8));
        }

        public byte[] ToBytes(bool lendian = true)
        {
            var arr = new byte[this.pn.Length * WORDSIZE];
            for (int i = 0; i < this.pn.Length; i++)
                Buffer.BlockCopy(Utils.ToBytes((dynamic)this.pn[i], true), 0, arr, WORDSIZE * i, WORDSIZE);
            if (!lendian)
                Array.Reverse(arr);
            return arr;
        }

        public override string ToString()
        {
            return Encoder.EncodeData(ToBytes().Reverse().ToArray());
        }

        public static bool TryParse(int size, string hex, out NumArray<T> result)
        {
            if (hex == null)
                throw new ArgumentNullException("hex");
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);
            result = null;
            if (hex.Length != size * WORDSIZE * 2)
                return false;
            if (!((HexEncoder)Encoders.Hex).IsValid(hex))
                return false;
            result = new NumArray<T>(size, hex);
            return true;
        }

        public override int GetHashCode()
        {
            return this.pn.GetHashCode();
        }

        protected static T[] Multiply(T[] value1, T[] value2)
        {
            var target = new T[value1.Length];

            for (int i2 = 0; i2 < value2.Length; i2++)
            {
                T carry = default(T);

                if ((dynamic)value2[i2] == default(T) && (dynamic)carry == default(T))
                    continue;

                for (int i1 = 0; i1 < value1.Length; i1++)
                {
                    if ((dynamic)value1[i1] == default(T) && (dynamic)carry == default(T))
                        continue;

                    ulong res = (((i1 + i2) < target.Length) ? (ulong)(dynamic)target[i1 + i2] : 0) + (dynamic)value1[i1] * value2[i2] + carry;
                    if (res == 0)
                        continue;

                    if (res >= ((ulong)1 << BITS))
                    {
                        carry = (T)(dynamic)(res >> BITS);
                        res &= (((ulong)1 << BITS) - 1);
                    }
                    else
                    {
                        carry = default(T);
                    }

                    if ((i1 + i2) >= target.Length)
                        throw new OverflowException();
                    
                    target[i1 + i2] = (T)(dynamic)res;
                }
            }

            return target;
        }
    }
}
