using System;
using Xunit;

namespace Lokad.ContentAddr.Tests
{
    public sealed class HashTests
    {
        [Fact]
        public void ByteConstructor()
        {
            Hash hash = new Hash([
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xAA, 0xBB,
                0xCC, 0xDD, 0xEE, 0xFF
            ]);

            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void ByteConstructorThrows()
        {
            try
            {
                _ = new Hash([
                    0x00, 0x11, 0x22, 0x33,
                    0x44, 0x55, 0x66, 0x77,
                    0x88, 0x99, 0xAA, 0xBB,
                    0xCC, 0xDD, 0xEE // Missing 16th
                ]);

                Assert.True(false);
            }
            catch (ArgumentException)
            {
                ;
            }
        }

        [Fact]
        public void StringConstructor()
        {
            Hash hash = new Hash("00112233445566778899AABBCCDDEEFF");
            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void StringConstructorThrows()
        {
            try
            {
                _ = new Hash("00112233445566778899AABBCCDDEEFFbad");

                Assert.True(false);
            }
            catch (ArgumentException)
            {
                ;
            }
        }

        [Fact]
        public void HashEqual()
        {
            Hash a = new Hash("00112233445566778899AABBCCDDEEFF");
            Hash b = new Hash("00112233445566778899AABBCCDDEEFF");
            Hash c = new Hash("00112233445566778899AABBCCDDFFEE");

            Assert.True(a.Equals(b));
            Assert.False(a.Equals(c));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals(null));
            Assert.False(a.Equals("00112233445566778899AABBCCDDEEFF"));
        }

        [Fact]
        public void HashCode()
        {
            Hash hash = new Hash("00112233445566778899AABBCCDDEEFF");
            Assert.Equal(0x44556677, hash.GetHashCode());
        }

        [Fact]
        public void StartsWith()
        {
            Hash hash = new Hash("00112233445566778899AABBCCDDEEFF");
            Assert.True(hash.StartsWith("00112233445566778899AABBCCDDEEFF"));
            Assert.True(hash.StartsWith("00112233445566778899AABBCCDDEE"));
            Assert.True(hash.StartsWith("0011223344"));
            Assert.True(hash.StartsWith("00112233445566778899aabbccddeeff"));
            Assert.False(hash.StartsWith("00112233445566778899AABBCCDDEEFFNope"));
            Assert.False(hash.StartsWith("001122334455667788Nope"));
        }

        [Fact]
        public void Compare()
        {
            Hash a = new Hash("00112233445566778899AABBCCDDEEFF");
            Hash b = new Hash("00112233445566778899AABBCCDDEEFF");
            Hash c = new Hash("00112233445566778899AABBCCDDFFEE");
            Hash d = new Hash("00112233445577668899AABBCCDDFFEE");

            Assert.Equal(0, a.CompareTo(b));
            Assert.Equal(-1, a.CompareTo(c));
            Assert.Equal(1, c.CompareTo(a));
            Assert.Equal(-1, c.CompareTo(d));
            Assert.Equal(1, d.CompareTo(c));
        }

        [Fact]
        public void TryParse()
        {
            Assert.True(Hash.TryParse("00112233445566778899AABBCCDDEEFF", out Hash hash));
            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void TryParseFail()
        {
            Assert.False(Hash.TryParse("bad", out Hash hash));
            Assert.Equal("00000000000000000000000000000000", hash.ToString());
        }

        [Fact]
        public void ToBase64()
        {
            Hash hash = new Hash("00112233445566778899AABBCCDDEEFF");
            Assert.Equal("ABEiM0RVZneImaq7zN3u/w==", hash.ToBase64());
        }

        [Fact]
        public void FromBytes()
        {
            Hash hash = Hash.FromBytes([
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xAA, 0xBB,
                0xCC, 0xDD, 0xEE, 0xFF
            ]);

            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void FromBytesOffset()
        {
            Hash hash = Hash.FromBytes([
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xAA, 0xBB,
                0xCC, 0xDD, 0xEE, 0xFF
            ], 4);

            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void FromBytesRospan()
        {
            Hash hash = Hash.FromBytes(new byte[] {
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xAA, 0xBB,
                0xCC, 0xDD, 0xEE, 0xFF
            }.AsSpan(4));

            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void ToBytes()
        {
            byte[] bytes = new byte[16];
            Hash hash = new Hash("00112233445566778899AABBCCDDEEFF");

            hash.ToBytes(bytes);

            Assert.Equal(hash, Hash.FromBytes(bytes));
        }

        [Fact]
        public void ToBytesOffset()
        {
            byte[] bytes = new byte[20];
            Hash hash = new Hash("00112233445566778899AABBCCDDEEFF");

            hash.ToBytes(bytes, 4);

            Assert.Equal(hash, Hash.FromBytes(bytes, 4));
        }

        [Fact]
        public void ToBytesSpan()
        {
            byte[] bytes = new byte[20];
            Hash hash = new Hash("00112233445566778899AABBCCDDEEFF");

            hash.ToBytes(bytes.AsSpan(4));

            Assert.Equal(hash, Hash.FromBytes(bytes.AsSpan(4)));
        }

        [Fact]
        public void Size()
        {
            Assert.Equal(16, Hash.Size);
        }

        [Fact]
        public void ToJson()
        {
            Hash hash = new Hash("00112233445566778899AABBCCDDEEFF");
            string json = System.Text.Json.JsonSerializer.Serialize(hash);

            Assert.Equal("\"00112233445566778899AABBCCDDEEFF\"", json);
        }


        [Fact]
        public void FromJson()
        {
            Hash expected = new Hash("00112233445566778899AABBCCDDEEFF");
            const string json = "\"00112233445566778899AABBCCDDEEFF\"";
            Hash hash = System.Text.Json.JsonSerializer.Deserialize<Hash>(json);

            Assert.Equal(expected, hash);
        }
    }
}
