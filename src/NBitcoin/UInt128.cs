using System;
using System.Numerics;
using NBitcoin.DataEncoders;

namespace NBitcoin
{
    public class uint128 : BigInteger128
    {
        public class MutableUint128 : IBitcoinSerializable
        {
            private uint128 _Value;

            public uint128 Value
            {
                get
                {
                    return this._Value;
                }
                set
                {
                    this._Value = value;
                }
            }

            public uint128 MaxValue => uint128.Parse("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

            public MutableUint128()
            {
                this._Value = Zero;
            }
            public MutableUint128(uint128 value)
            {
                this._Value = value;
            }

            public void ReadWrite(BitcoinStream stream)
            {
                if (stream.Serializing)
                {
                    byte[] b = this.Value.ToBytes();
                    stream.ReadWrite(ref b);
                }
                else
                {
                    var b = new byte[this.Value.width];
                    stream.ReadWrite(ref b);
                    this._Value = new uint128(b);
                }
            }
        }

        private static readonly uint128 _Zero = new uint128();

        public static uint128 Zero
        {
            get { return _Zero; }
        }

        private static readonly uint128 _One = new uint128(1);
        public static uint128 One
        {
            get { return _One; }
        }

        public uint128() : base()
        {
        }

        public uint128(BigInteger value) : base(value)
        {
        }

        public uint128(uint128 value) : this(value.value)
        {
        }

        public uint128(string hex) : base(hex)
        {
        }

        public static uint128 Parse(string hex)
        {
            return new uint128(hex);
        }

        public static bool TryParse(string hex, out uint128 result)
        {
            result = null;

            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);

            if (!((HexEncoder)Encoders.Hex).IsValid(hex))
                return false;

            try
            {
                result = new uint128(hex);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public uint128(ulong b) : base(b)
        {
        }

        public uint128(byte[] vch, bool lendian = true) : base(vch, lendian)
        {
        }

        public static uint128 operator >>(uint128 a, int shift)
        {
            return new uint128(a.ShiftRight(shift));
        }

        public static uint128 operator <<(uint128 a, int shift)
        {
            return new uint128(a.ShiftLeft(shift));
        }

        public static uint128 operator -(uint128 a, uint128 b)
        {
            return new uint128(a.Subtract(b.value));
        }

        public static uint128 operator +(uint128 a, uint128 b)
        {
            return new uint128(a.Add(b.value));
        }

        public static uint128 operator *(uint128 a, uint128 b)
        {
            return new uint128(a.Multiply(b.value));
        }

        public static uint128 operator /(uint128 a, uint128 b)
        {
            return new uint128(a.Divide(b.value));
        }

        public static uint128 operator %(uint128 a, uint128 b)
        {
            return new uint128(a.Mod(b.value));
        }

        public uint128(byte[] vch) : this(vch, true)
        {
        }

        public static bool operator <(uint128 a, uint128 b)
        {
            return Comparison(a, b) < 0;
        }

        public static bool operator >(uint128 a, uint128 b)
        {
            return Comparison(a, b) > 0;
        }

        public static bool operator <=(uint128 a, uint128 b)
        {
            return Comparison(a, b) <= 0;
        }

        public static bool operator >=(uint128 a, uint128 b)
        {
            return Comparison(a, b) >= 0;
        }

        public static bool operator ==(uint128 a, uint128 b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (((object)a == null) != ((object)b == null))
                return false;

            return Comparison(a, b) == 0;
        }

        public static bool operator !=(uint128 a, uint128 b)
        {
            return !(a == b);
        }

        public static bool operator ==(uint128 a, ulong b)
        {
            return (a == new uint128(b));
        }

        public static bool operator !=(uint128 a, ulong b)
        {
            return !(a == new uint128(b));
        }

        public static implicit operator uint128(ulong value)
        {
            return new uint128(value);
        }

        public MutableUint128 AsBitcoinSerializable()
        {
            return new MutableUint128(this);
        }
    }
}