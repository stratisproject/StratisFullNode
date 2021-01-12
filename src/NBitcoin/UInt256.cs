using System;
using System.Numerics;
using NBitcoin.DataEncoders;

namespace NBitcoin
{
    public class uint256 : BigInteger256
    {
        public class MutableUint256 : IBitcoinSerializable
        {
            private uint256 _Value;

            public uint256 Value
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

            public uint256 MaxValue => uint256.Parse("ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

            public MutableUint256()
            {
                this._Value = Zero;
            }
            public MutableUint256(uint256 value)
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
                    this._Value = new uint256(b);
                }
            }
        }
        
        private static readonly uint256 _Zero = new uint256();

        public static uint256 Zero
        {
            get { return _Zero; }
        }

        private static readonly uint256 _One = new uint256(1);
        public static uint256 One
        {
            get { return _One; }
        }
        
        public uint256() : base()
        {
        }

        public uint256(BigInteger value) : base(value)
        {
        }
       
        public uint256(uint256 value) : this(value.value)
        {
        }
       
        public uint256(string hex) : base(hex)
        {
        }

        public static uint256 Parse(string hex)
        {
            return new uint256(hex);
        }

        public static bool TryParse(string hex, out uint256 result)
        {
            result = null;

            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);

            if (!((HexEncoder)Encoders.Hex).IsValid(hex))
                return false;

            try
            {
                result = new uint256(hex);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        public uint256(ulong b) : base(b)
        {
        }

        public uint256(byte[] vch, bool lendian = true) : base(vch, lendian)
        {
        }

        public static uint256 operator >>(uint256 a, int shift)
        {
            return new uint256(a.ShiftRight(shift));
        }

        public static uint256 operator <<(uint256 a, int shift)
        {
            return new uint256(a.ShiftLeft(shift));
        }

        public static uint256 operator -(uint256 a, uint256 b)
        {
            return new uint256(a.Subtract(b.value));
        }

        public static uint256 operator +(uint256 a, uint256 b)
        {
            return new uint256(a.Add(b.value));
        }

        public static uint256 operator *(uint256 a, uint256 b)
        {
            return new uint256(a.Multiply(b.value));
        }

        public static uint256 operator /(uint256 a, uint256 b)
        {
            return new uint256(a.Divide(b.value));
        }

        public static uint256 operator %(uint256 a, uint256 b)
        {
            return new uint256(a.Mod(b.value));
        }

        public uint256(byte[] vch) : this(vch, true)
        {
        }
        
        public static bool operator <(uint256 a, uint256 b)
        {
            return Comparison(a, b) < 0;
        }

        public static bool operator >(uint256 a, uint256 b)
        {
            return Comparison(a, b) > 0;
        }

        public static bool operator <=(uint256 a, uint256 b)
        {
            return Comparison(a, b) <= 0;
        }

        public static bool operator >=(uint256 a, uint256 b)
        {
            return Comparison(a, b) >= 0;
        }

        public static bool operator ==(uint256 a, uint256 b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (((object)a == null) != ((object)b == null))
                return false;

            return Comparison(a, b) == 0;
        }

        public static bool operator !=(uint256 a, uint256 b)
        {
            return !(a == b);
        }

        public static bool operator ==(uint256 a, ulong b)
        {
            return (a == new uint256(b));
        }

        public static bool operator !=(uint256 a, ulong b)
        {
            return !(a == new uint256(b));
        }
        
        public static implicit operator uint256(ulong value)
        {
            return new uint256(value);
        }

        public MutableUint256 AsBitcoinSerializable()
        {
            return new MutableUint256(this);
        }
    }
}