using System;
using Xunit;

namespace Lokad.ContentAddr.Tests
{
    public sealed class hash
    {
        [Fact]
        public void byte_constructor()
        {
            var hash = new Hash(new byte[] {
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xAA, 0xBB,
                0xCC, 0xDD, 0xEE, 0xFF
            });

            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void byte_constructor_throws()
        {
            try
            {
                new Hash(new byte[] {
                    0x00, 0x11, 0x22, 0x33,
                    0x44, 0x55, 0x66, 0x77,
                    0x88, 0x99, 0xAA, 0xBB,
                    0xCC, 0xDD, 0xEE // Missing 16th
                });

                Assert.True(false);
            }
            catch (ArgumentException)
            {
                ;
            }
        }

        [Fact]
        public void string_constructor()
        {
            var hash = new Hash("00112233445566778899AABBCCDDEEFF");
            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void string_constructor_throws()
        {
            try
            {
                new Hash("00112233445566778899AABBCCDDEEFFbad");

                Assert.True(false);
            }
            catch (ArgumentException)
            {
                ;
            }
        }

        [Fact]
        public void equal()
        {
            var a = new Hash("00112233445566778899AABBCCDDEEFF");
            var b = new Hash("00112233445566778899AABBCCDDEEFF");
            var c = new Hash("00112233445566778899AABBCCDDFFEE");

            Assert.True(a.Equals(b));
            Assert.False(a.Equals(c));
            Assert.True(a.Equals((object)b));
            Assert.False(a.Equals(null));
            Assert.False(a.Equals("00112233445566778899AABBCCDDEEFF"));
        }

        [Fact]
        public void gethashcode()
        {
            var hash = new Hash("00112233445566778899AABBCCDDEEFF");
            Assert.Equal(0x44556677, hash.GetHashCode());
        }

        [Fact]
        public void startswith()
        {
            var hash = new Hash("00112233445566778899AABBCCDDEEFF");
            Assert.True(hash.StartsWith("00112233445566778899AABBCCDDEEFF"));
            Assert.True(hash.StartsWith("00112233445566778899AABBCCDDEE"));
            Assert.True(hash.StartsWith("0011223344"));
            Assert.True(hash.StartsWith("00112233445566778899aabbccddeeff"));
            Assert.False(hash.StartsWith("00112233445566778899AABBCCDDEEFFNope"));
            Assert.False(hash.StartsWith("001122334455667788Nope"));
        }

        [Fact]
        public void compare()
        {
            var a = new Hash("00112233445566778899AABBCCDDEEFF");
            var b = new Hash("00112233445566778899AABBCCDDEEFF");
            var c = new Hash("00112233445566778899AABBCCDDFFEE");
            var d = new Hash("00112233445577668899AABBCCDDFFEE");

            Assert.Equal(0, a.CompareTo(b));
            Assert.Equal(-1, a.CompareTo(c));
            Assert.Equal(1, c.CompareTo(a));
            Assert.Equal(-1, c.CompareTo(d));
            Assert.Equal(1, d.CompareTo(c));
        }

        [Fact]
        public void try_parse()
        {
            Assert.True(Hash.TryParse("00112233445566778899AABBCCDDEEFF", out var hash));
            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void try_parse_fail()
        {
            Assert.False(Hash.TryParse("bad", out var hash));
            Assert.Equal("00000000000000000000000000000000", hash.ToString());
        }

        [Fact]
        public void to_base64()
        {
            var hash = new Hash("00112233445566778899AABBCCDDEEFF");
            Assert.Equal("ABEiM0RVZneImaq7zN3u/w==", hash.ToBase64());
        }

        [Fact]
        public void from_bytes()
        {
            var hash = Hash.FromBytes(new byte[] {
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xAA, 0xBB,
                0xCC, 0xDD, 0xEE, 0xFF
            });

            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void from_bytes_offset()
        {
            var hash = Hash.FromBytes(new byte[] {
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xAA, 0xBB,
                0xCC, 0xDD, 0xEE, 0xFF
            }, 4);

            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void from_bytes_rospan()
        {
            var hash = Hash.FromBytes(new byte[] {
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x11, 0x22, 0x33,
                0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xAA, 0xBB,
                0xCC, 0xDD, 0xEE, 0xFF
            }.AsSpan(4));

            Assert.Equal("00112233445566778899AABBCCDDEEFF", hash.ToString());
        }

        [Fact]
        public void to_bytes()
        {
            var bytes = new byte[16];
            var hash = new Hash("00112233445566778899AABBCCDDEEFF");

            hash.ToBytes(bytes);

            Assert.Equal(hash, Hash.FromBytes(bytes));
        }

        [Fact]
        public void to_bytes_offset()
        {
            var bytes = new byte[20];
            var hash = new Hash("00112233445566778899AABBCCDDEEFF");

            hash.ToBytes(bytes, 4);

            Assert.Equal(hash, Hash.FromBytes(bytes, 4));
        }

        [Fact]
        public void to_bytes_span()
        {
            var bytes = new byte[20];
            var hash = new Hash("00112233445566778899AABBCCDDEEFF");

            hash.ToBytes(bytes.AsSpan(4));

            Assert.Equal(hash, Hash.FromBytes(bytes.AsSpan(4)));
        }

        [Fact]
        public void size()
        {
            Assert.Equal(16, Hash.Size);
        }
    }
}
