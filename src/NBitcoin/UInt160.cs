using System;
using System.Numerics;
using NBitcoin.DataEncoders;

namespace NBitcoin
{
    public class uint160 : BigInteger160
    {
        public class MutableUint160 : IBitcoinSerializable
        {
            private uint160 _Value;

            public uint160 Value
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

            public uint160 MaxValue => uint160.Parse("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

            public MutableUint160()
            {
                this._Value = Zero;
            }
            public MutableUint160(uint160 value)
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
                    this._Value = new uint160(b);
                }
            }
        }

        private static readonly uint160 _Zero = new uint160();

        public static uint160 Zero
        {
            get { return _Zero; }
        }

        private static readonly uint160 _One = new uint160(1);
        public static uint160 One
        {
            get { return _One; }
        }

        public uint160() : base()
        {
        }

        public uint160(BigInteger value) : base(value)
        {
        }

        public uint160(uint160 value) : this(value.value)
        {
        }

        public uint160(string hex) : base(hex)
        {
        }

        public static uint160 Parse(string hex)
        {
            return new uint160(hex);
        }

        public static bool TryParse(string hex, out uint160 result)
        {
            result = null;

            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);

            if (!((HexEncoder)Encoders.Hex).IsValid(hex))
                return false;

            try
            {
                result = new uint160(hex);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public uint160(ulong b) : base(b)
        {
        }

        public uint160(byte[] vch, bool lendian = true) : base(vch, lendian)
        {
        }

        public static uint160 operator >>(uint160 a, int shift)
        {
            return new uint160(a.ShiftRight(shift));
        }

        public static uint160 operator <<(uint160 a, int shift)
        {
            return new uint160(a.ShiftLeft(shift));
        }

        public static uint160 operator -(uint160 a, uint160 b)
        {
            return new uint160(a.Subtract(b.value));
        }

        public static uint160 operator +(uint160 a, uint160 b)
        {
            return new uint160(a.Add(b.value));
        }

        public static uint160 operator *(uint160 a, uint160 b)
        {
            return new uint160(a.Multiply(b.value));
        }

        public static uint160 operator /(uint160 a, uint160 b)
        {
            return new uint160(a.Divide(b.value));
        }

        public static uint160 operator %(uint160 a, uint160 b)
        {
            return new uint160(a.Mod(b.value));
        }

        public uint160(byte[] vch) : this(vch, true)
        {
        }

        public static bool operator <(uint160 a, uint160 b)
        {
            return Comparison(a, b) < 0;
        }

        public static bool operator >(uint160 a, uint160 b)
        {
            return Comparison(a, b) > 0;
        }

        public static bool operator <=(uint160 a, uint160 b)
        {
            return Comparison(a, b) <= 0;
        }

        public static bool operator >=(uint160 a, uint160 b)
        {
            return Comparison(a, b) >= 0;
        }

        public static bool operator ==(uint160 a, uint160 b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (((object)a == null) != ((object)b == null))
                return false;

            return Comparison(a, b) == 0;
        }

        public static bool operator !=(uint160 a, uint160 b)
        {
            return !(a == b);
        }

        public static bool operator ==(uint160 a, ulong b)
        {
            return (a == new uint160(b));
        }

        public static bool operator !=(uint160 a, ulong b)
        {
            return !(a == new uint160(b));
        }

        public static implicit operator uint160(ulong value)
        {
            return new uint160(value);
        }

        public MutableUint160 AsBitcoinSerializable()
        {
            return new MutableUint160(this);
        }
    }
}