﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NBitcoin.BouncyCastle.Math;
using Stratis.Bitcoin.Tests.Common;
using Xunit;

namespace NBitcoin.Tests
{
    public class uint256_tests
    {
        private readonly Network networkMain;

        public uint256_tests()
        {
            this.networkMain = KnownNetworks.Main;
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void uintTests()
        {
            var v = new uint256("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
            var v2 = new uint256("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
            var vless = new uint256("00000000fffffffffffffffffffffffffffffffffffffffffffffffffffffffe");
            var vplus = new uint256("00000001ffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

            Assert.Equal("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff", v.ToString());
            Assert.Equal(new uint256("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff"), v);
            Assert.Equal(new uint256("0x00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff"), v);
            Assert.Equal(uint256.Parse("0x00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff"), v);
            Assert.True(v < vplus);
            Assert.True(v > vless);
            uint256 unused;
            Assert.True(uint256.TryParse("0x00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff", out unused));
            Assert.True(uint256.TryParse("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff", out unused));
            Assert.True(uint256.TryParse("00000000ffffFFfFffffffffffffffffffffffffffffffffffffffffffffffff", out unused));
            Assert.False(uint256.TryParse("00000000gfffffffffffffffffffffffffffffffffffffffffffffffffffffff", out unused));
            Assert.False(uint256.TryParse("100000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff", out unused));
            Assert.False(uint256.TryParse("1100000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff", out unused));
            Assert.Throws<FormatException>(() => uint256.Parse("1100000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));
            Assert.Throws<FormatException>(() => uint256.Parse("100000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));
            uint256.Parse("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
            Assert.Throws<FormatException>(() => uint256.Parse("000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff"));

            Assert.True(v >= v2);
            Assert.True(v <= v2);
            Assert.False(v < v2);
            Assert.False(v > v2);

            Assert.True(v.ToBytes()[0] == 0xFF);
            Assert.True(v.ToBytes(false)[0] == 0x00);

            AssertEquals(v, new uint256(v.ToBytes()));
            AssertEquals(v, new uint256(v.ToBytes(false), false));

            Assert.Equal(0xFF, v.GetByte(0));
            Assert.Equal(0x00, v.GetByte(31));
            Assert.Equal(0x39, new uint256("39000001ffffffffffffffffffffffffffffffffffffffffffffffffffffffff").GetByte(31));
            Assert.Throws<ArgumentOutOfRangeException>(() => v.GetByte(32));

            // Test conversions between uint256 and BigInteger
            var a = System.Numerics.BigInteger.Parse("00010000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.AllowHexSpecifier);
            var b = new uint256("00010000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes();

            // Conversion from the uint256 byte representation to BigInteger can be done directly.
            var c = new System.Numerics.BigInteger(b);
            
            Assert.True(a == c);
            
            var a1 = new uint256("00010000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
            var b1 = System.Numerics.BigInteger.Parse("00010000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff", NumberStyles.AllowHexSpecifier).ToByteArray();

            // The uint256 constructor requires a 256 bit array, so we have to right-pad the BigInteger byte array representation.
            var newArray = new byte[32];
            Array.Copy(b1, 0, newArray, 0, b1.Length);

            var c1 = new uint256(newArray);

            Assert.True(a1 == c1);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void uintSerializationTests()
        {
            var ms = new MemoryStream();
            var stream = new BitcoinStream(ms, true);
            stream.ConsensusFactory = this.networkMain.Consensus.ConsensusFactory;
            
            var v = new uint256("00000000ffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
            var vless = new uint256("00000000fffffffffffffffffffffffffffffffffffffffffffffffffffffffe");
            var vplus = new uint256("00000001ffffffffffffffffffffffffffffffffffffffffffffffffffffffff");

            stream.ReadWrite(ref v);
            Assert.NotNull(v);

            ms.Position = 0;
            stream = new BitcoinStream(ms, false);
            stream.ConsensusFactory = this.networkMain.Consensus.ConsensusFactory;

            uint256 v2 = uint256.Zero;
            stream.ReadWrite(ref v2);
            Assert.Equal(v, v2);

            v2 = null;
            ms.Position = 0;
            stream.ReadWrite(ref v2);
            Assert.Equal(v, v2);

            var vs = new List<uint256>()
            {
                v,vless,vplus
            };

            ms = new MemoryStream();
            stream = new BitcoinStream(ms, true);
            stream.ConsensusFactory = this.networkMain.Consensus.ConsensusFactory;
            stream.ReadWrite(ref vs);
            Assert.True(vs.Count == 3);

            ms.Position = 0;
            stream = new BitcoinStream(ms, false);
            stream.ConsensusFactory = this.networkMain.Consensus.ConsensusFactory;
            var vs2 = new List<uint256>();
            stream.ReadWrite(ref vs2);
            Assert.True(vs2.SequenceEqual(vs));

            ms.Position = 0;
            vs2 = null;
            stream.ReadWrite(ref vs2);
            Assert.True(vs2.SequenceEqual(vs));
        }

        private void AssertEquals(uint256 a, uint256 b)
        {
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.True(a == b);
        }
    }
}
