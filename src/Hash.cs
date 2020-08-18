using System;
using System.Diagnostics.Contracts;
using System.IO;

namespace Lokad.ContentAddr
{
    /// <summary> A memory-tight representation of a 128-bit hash. </summary>
    /// <remarks> Value type, uses exactly 16 bytes. </remarks>
    public struct Hash : IEquatable<Hash>
    {
        /// <summary> The first 64 bits of the 128-bit hash. </summary>
        public ulong HashLeft { get; private set; }

        /// <summary> The last 64 bits of the 128-bit hash. </summary>
        public ulong HashRight { get; private set; }

        /// <summary> The size of a <see cref="Hash"/>, in bytes. </summary>
        public const int Size = 2 * sizeof(ulong);

        /// <summary> Construct from 128 bits (16 bytes). </summary>
        public Hash(byte[] bytes)
        {
            if (bytes.Length != 16)
                throw new ArgumentException("Expected a 16-byte hash.", nameof(bytes));

            // Convert the byte[16] to two ulong, such that printing
            // the ulong as hex yields the same output as printing the
            // bytes as hex. BitConverter does not work here, because it
            // inverts the right-to-left ordering of bits.
            ulong left = 0, right = 0;

            for (var i = 7; i >= 0; --i)
            {
                left = (left << 8) | bytes[7 - i];
                right = (right << 8) | bytes[15 - i];
            }

            HashLeft = left;
            HashRight = right;
        }

        /// <summary> Private constructor. </summary>
        public Hash(ulong left, ulong right)
        {
            HashLeft = left;
            HashRight = right;
        }

        /// <summary> Convert from hex 16-byte string. </summary>
        public Hash(string hex)
        {
            if (hex.Length != 32)
                throw new ArgumentException("Expected a 32-character hash.", nameof(hex));

            HashLeft = Convert.ToUInt64(hex.Substring(0, 16), 16);
            HashRight = Convert.ToUInt64(hex.Substring(16), 16);
        }

        /// <summary> Display as hex string. </summary>
        public override string ToString()
        {
#if NETCOREAPP2_1
            return string.Create(32, this, PrintToSpan);
#else    
            var chars = new char[32];
            PrintToSpan(chars, this);
            return new string(chars);
#endif
        }

        /// <summary>
        ///     Print the hash in hexadecimal format to a 32-char span.
        /// </summary>
        public static void PrintToSpan(Span<char> chars, Hash hash)
        {
            ToHex(chars, (uint)(hash.HashLeft >> 32));
            ToHex(chars.Slice(8), (uint)hash.HashLeft);
            ToHex(chars.Slice(16), (uint)(hash.HashRight >> 32));
            ToHex(chars.Slice(24), (uint)hash.HashRight);

            void ToHex(Span<char> span, uint ui)
            {
                Span<char> c = Chars;

                span[0] = c[(int)((ui >> 28) & 0x0F)];

                var i = (int)(ui & 0x0FFF_FFFF);

                span[1] = c[i >> 24];
                span[2] = c[(i >> 20) & 0x0F];
                span[3] = c[(i >> 16) & 0x0F];
                span[4] = c[(i >> 12) & 0x0F];
                span[5] = c[(i >> 8) & 0x0F];
                span[6] = c[(i >> 4) & 0x0F];
                span[7] = c[i & 0x0F];
            }
        }

        private static readonly char[] Chars = new[] {
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'
        };

        public bool StartsWith(string prefix) =>
            $"{HashLeft:X16}{HashRight:X16}".StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        #region Equality

        [Pure]
        public bool Equals(Hash other) =>
            HashLeft == other.HashLeft && HashRight == other.HashRight;

        public override bool Equals(object obj) =>
            obj is Hash h && Equals(h);

        [Pure]
        public override int GetHashCode() =>
            (int)HashLeft;

        [Pure]
        public int CompareTo(Hash other) =>
            HashLeft == other.HashLeft
                ? HashRight.CompareTo(other.HashRight)
                : HashLeft.CompareTo(other.HashLeft);

        #endregion

        /// <summary> Output the hash as <see cref="Size"/> bytes in the target array. </summary>
        /// <remarks> Reverse of <see cref="FromBytes(byte[])"/>. </remarks>       
        public void ToBytes(byte[] buffer, int offset) =>
            ToBytes(new Span<byte>(buffer, offset, Size));

        /// <summary> Output the hash as <see cref="Size"/> bytes in the target span. </summary>
        /// <remarks> Reverse of <see cref="FromBytes(byte[])"/>. </remarks>     
        public void ToBytes(Span<byte> span)
        {
            var a = HashLeft;
            var b = HashRight;

            for (var i = 7; i >= 0; --i)
            {
                span[i] = (byte)a;
                span[i + 8] = (byte)b;

                a >>= 8;
                b >>= 8;
            }
        }

        public static Hash FromBytes(byte[] h) => FromBytes(h, 0);

        public static Hash FromBytes(byte[] h, int o) => FromBytes(h.AsSpan(o));

        /// <summary> Read the hash from 16 bytes in the target array. </summary>
        /// <remarks> Inverse of <see cref="ToBytes"/>. </remarks>
        public static Hash FromBytes(ReadOnlySpan<byte> h)
        {
            var o = 0;
            var a1 = ((uint)h[o] << 24) | (uint)(h[o + 1] << 16) | (uint)(h[o + 2] << 8) | h[o + 3];
            o += 4;
            var a2 = ((uint)h[o] << 24) | (uint)(h[o + 1] << 16) | (uint)(h[o + 2] << 8) | h[o + 3];
            o += 4;
            var b1 = ((uint)h[o] << 24) | (uint)(h[o + 1] << 16) | (uint)(h[o + 2] << 8) | h[o + 3];
            o += 4;
            var b2 = ((uint)h[o] << 24) | (uint)(h[o + 1] << 16) | (uint)(h[o + 2] << 8) | h[o + 3];

            var a = ((ulong)a1 << 32) | a2;
            var b = ((ulong)b1 << 32) | b2;

            return new Hash(a, b);
        }

        /// <summary> Attempt to parse a hash from a 32-character hex string. </summary>
        public static bool TryParse(string hash, out Hash parsed)
        {
            parsed = default;

            if (hash.Length != 32)
                return false;

            try
            {
                parsed = new Hash(hash);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary> Encode the hash as base-64, for use in Content-MD5 HTTP header. </summary>
        [Pure]
        public string ToBase64()
        {
            var bytes = new byte[16];
            ToBytes(bytes, 0);
            return Convert.ToBase64String(bytes);
        }
    }

    public static class HashSerialization
    {
        /// <summary> Read a <see cref="Hash"/> from binary. </summary>
        /// <see cref="Write"/>
        public static Hash ReadHash(this BinaryReader read)
        {
            var l = read.ReadUInt64();
            var r = read.ReadUInt64();
            return new Hash(l, r);
        }

        /// <summary> Read a <see cref="Hash"/> from binary. </summary>
        /// <see cref="Hash.FromBytes"/>
        public static Hash ReadHashBytes(this BinaryReader r)
        {
            var a = 0UL;
            for (var i = 64 - 8; i >= 0; i -= 8)
                a += (ulong)r.ReadByte() << i;

            var b = 0UL;
            for (var i = 64 - 8; i >= 0; i -= 8)
                b += (ulong)r.ReadByte() << i;

            return new Hash(a, b);
        }

        /// <summary> Write a <see cref="Hash"/> to binary. </summary>
        /// <remarks>
        ///     The serialization format is NOT the original byte[16] of the 
        ///     MD5 hash. It is instead optimized for being converted to string.
        /// </remarks>
        /// <see cref="ReadHash"/>
        public static void Write(this BinaryWriter w, Hash h)
        {
            w.Write(h.HashLeft);
            w.Write(h.HashRight);
        }

        /// <summary> Write a <see cref="Hash"/> as its actual byte sequence. </summary>
        /// <see cref="ReadHashBytes"/>
        /// <see cref="Hash.ToBytes"/>
        public static void WriteBytes(this BinaryWriter w, Hash h)
        {
            var a = h.HashLeft;
            var b = h.HashRight;

            for (var i = 64 - 8; i >= 0; i -= 8)
                w.Write((byte)(a >> i));

            for (var i = 64 - 8; i >= 0; i -= 8)
                w.Write((byte)(b >> i));
        }
    }
}
