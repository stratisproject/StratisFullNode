using System;
using System.Linq;
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
                    var b = new byte[WIDTH_BYTE];
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

        private const int WIDTH = 256 / 32;

        private uint256(uint[] array) : base(array)
        {
            if (array.Length != WIDTH)
                throw new ArgumentOutOfRangeException();
        }

        private uint256(BigInteger256 value) : base(value)
        {
        }

        public uint256(uint256 value) : this(value.ToUIntArray())
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

        private const int WIDTH_BYTE = 256 / 8;

        public uint256(ulong b) : base(b)
        {
        }

        public uint256(byte[] vch, bool lendian = true) : base(vch, lendian)
        {
        }

        public static uint256 operator >>(uint256 a, int shift)
        {
            return new uint256(ShiftRight(a, shift));
        }

        public static uint256 operator <<(uint256 a, int shift)
        {
            return new uint256(ShiftLeft(a, shift));
        }

        public static uint256 operator -(uint256 a, uint256 b)
        {
            return new uint256(Subtract(a, b));
        }

        public static uint256 operator +(uint256 a, uint256 b)
        {
            return new uint256(Add(a, b));
        }

        public static uint256 operator *(uint256 a, uint256 b)
        {
            return new uint256(Multiply(a, b));
        }

        public static uint256 operator /(uint256 a, uint256 b)
        {
            return new uint256(Divide(a, b));
        }

        public static uint256 operator %(uint256 a, uint256 b)
        {
            return new uint256(Mod(a, b));
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

        public int GetSerializeSize()
        {
            return WIDTH_BYTE;
        }

        public int Size
        {
            get
            {
                return WIDTH_BYTE;
            }
        }

        public ulong GetLow64()
        {
            var pn = ToUIntArray();
            return pn[0] | (ulong)pn[1] << 32;
        }

        public uint GetLow32()
        {
            var pn = ToUIntArray();
            return pn[0];
        }

        public ulong GetULong(int position)
        {
            var pn = ToUIntArray();
            switch (position)
            {
                case 0:
                    return (ulong)pn[0] + (ulong)((ulong)pn[1] << 32);
                case 1:
                    return (ulong)pn[2] + (ulong)((ulong)pn[3] << 32);
                case 2:
                    return (ulong)pn[4] + (ulong)((ulong)pn[5] << 32);
                case 3:
                    return (ulong)pn[6] + (ulong)((ulong)pn[7] << 32);
                default:
                    throw new ArgumentOutOfRangeException("position should be less than 4", "position");
            }
        }
    }

    public class uint160
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
                    var b = new byte[WIDTH_BYTE];
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

        public uint160()
        {
        }

        public uint160(uint160 b)
        {
            this.pn0 = b.pn0;
            this.pn1 = b.pn1;
            this.pn2 = b.pn2;
            this.pn3 = b.pn3;
            this.pn4 = b.pn4;
        }

        public static uint160 Parse(string hex)
        {
            return new uint160(hex);
        }
        public static bool TryParse(string hex, out uint160 result)
        {
            if (hex == null)
                throw new ArgumentNullException("hex");
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hex = hex.Substring(2);
            result = null;
            if (hex.Length != WIDTH_BYTE * 2)
                return false;
            if (!((HexEncoder)Encoders.Hex).IsValid(hex))
                return false;
            result = new uint160(hex);
            return true;
        }

        private static readonly HexEncoder Encoder = new HexEncoder();
        private const int WIDTH_BYTE = 160 / 8;
        internal readonly UInt32 pn0;
        internal readonly UInt32 pn1;
        internal readonly UInt32 pn2;
        internal readonly UInt32 pn3;
        internal readonly UInt32 pn4;

        public byte GetByte(int index)
        {
            int uintIndex = index / sizeof(uint);
            int byteIndex = index % sizeof(uint);
            UInt32 value;
            switch (uintIndex)
            {
                case 0:
                    value = this.pn0;
                    break;
                case 1:
                    value = this.pn1;
                    break;
                case 2:
                    value = this.pn2;
                    break;
                case 3:
                    value = this.pn3;
                    break;
                case 4:
                    value = this.pn4;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("index");
            }
            return (byte)(value >> (byteIndex * 8));
        }

        public override string ToString()
        {
            return Encoder.EncodeData(ToBytes().Reverse().ToArray());
        }

        public uint160(ulong b)
        {
            this.pn0 = (uint)b;
            this.pn1 = (uint)(b >> 32);
            this.pn2 = 0;
            this.pn3 = 0;
            this.pn4 = 0;
        }

        public uint160(byte[] vch, bool lendian = true)
        {
            if (vch.Length != WIDTH_BYTE)
            {
                throw new FormatException("the byte array should be 160 byte long");
            }

            if (!lendian)
                vch = vch.Reverse().ToArray();

            this.pn0 = Utils.ToUInt32(vch, 4 * 0, true);
            this.pn1 = Utils.ToUInt32(vch, 4 * 1, true);
            this.pn2 = Utils.ToUInt32(vch, 4 * 2, true);
            this.pn3 = Utils.ToUInt32(vch, 4 * 3, true);
            this.pn4 = Utils.ToUInt32(vch, 4 * 4, true);

        }

        public uint160(string str)
        {
            this.pn0 = 0;
            this.pn1 = 0;
            this.pn2 = 0;
            this.pn3 = 0;
            this.pn4 = 0;
            str = str.Trim();

            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                str = str.Substring(2);

            byte[] bytes = Encoder.DecodeData(str).Reverse().ToArray();
            if (bytes.Length != WIDTH_BYTE)
                throw new FormatException("Invalid hex length");
            this.pn0 = Utils.ToUInt32(bytes, 4 * 0, true);
            this.pn1 = Utils.ToUInt32(bytes, 4 * 1, true);
            this.pn2 = Utils.ToUInt32(bytes, 4 * 2, true);
            this.pn3 = Utils.ToUInt32(bytes, 4 * 3, true);
            this.pn4 = Utils.ToUInt32(bytes, 4 * 4, true);

        }

        public uint160(byte[] vch)
            : this(vch, true)
        {
        }

        public override bool Equals(object obj)
        {
            var item = obj as uint160;
            if (item == null)
                return false;
            bool equals = true;
            equals &= this.pn0 == item.pn0;
            equals &= this.pn1 == item.pn1;
            equals &= this.pn2 == item.pn2;
            equals &= this.pn3 == item.pn3;
            equals &= this.pn4 == item.pn4;
            return equals;
        }

        public static bool operator ==(uint160 a, uint160 b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (((object)a == null) || ((object)b == null))
                return false;

            bool equals = true;
            equals &= a.pn0 == b.pn0;
            equals &= a.pn1 == b.pn1;
            equals &= a.pn2 == b.pn2;
            equals &= a.pn3 == b.pn3;
            equals &= a.pn4 == b.pn4;
            return equals;
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

        private static int Comparison(uint160 a, uint160 b)
        {
            if (a.pn4 < b.pn4)
                return -1;
            if (a.pn4 > b.pn4)
                return 1;
            if (a.pn3 < b.pn3)
                return -1;
            if (a.pn3 > b.pn3)
                return 1;
            if (a.pn2 < b.pn2)
                return -1;
            if (a.pn2 > b.pn2)
                return 1;
            if (a.pn1 < b.pn1)
                return -1;
            if (a.pn1 > b.pn1)
                return 1;
            if (a.pn0 < b.pn0)
                return -1;
            if (a.pn0 > b.pn0)
                return 1;
            return 0;
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


        public byte[] ToBytes(bool lendian = true)
        {
            var arr = new byte[WIDTH_BYTE];
            Buffer.BlockCopy(Utils.ToBytes(this.pn0, true), 0, arr, 4 * 0, 4);
            Buffer.BlockCopy(Utils.ToBytes(this.pn1, true), 0, arr, 4 * 1, 4);
            Buffer.BlockCopy(Utils.ToBytes(this.pn2, true), 0, arr, 4 * 2, 4);
            Buffer.BlockCopy(Utils.ToBytes(this.pn3, true), 0, arr, 4 * 3, 4);
            Buffer.BlockCopy(Utils.ToBytes(this.pn4, true), 0, arr, 4 * 4, 4);
            if (!lendian)
                Array.Reverse(arr);
            return arr;
        }

        public MutableUint160 AsBitcoinSerializable()
        {
            return new MutableUint160(this);
        }

        public int GetSerializeSize()
        {
            return WIDTH_BYTE;
        }

        public int Size
        {
            get
            {
                return WIDTH_BYTE;
            }
        }

        public ulong GetLow64()
        {
            return this.pn0 | (ulong) this.pn1 << 32;
        }

        public uint GetLow32()
        {
            return this.pn0;
        }

        public override int GetHashCode()
        {
            return (int)this.pn0;
        }
    }
}