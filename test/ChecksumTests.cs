using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Lokad.ContentAddr.Tests
{
    public sealed class ChecksumTests
    {
        [Fact]
        public void CRC32_sample_value()
        {
            byte[] buffer = "123456789"u8.ToArray();

            uint crc = Checksum.CRC32(buffer, 0, 9);
            // See http://www.sunshine2k.de/coding/javascript/crc/crc_js.html
            // See http://www.lammertbies.nl/comm/info/crc-calculation.html
            Assert.Equal("CBF43926", crc.ToString("X8"));
        }


        [Fact]
        public void CRC32_sample_value_rospan()
        {
            byte[] buffer = "123456789"u8.ToArray();

            uint crc = Checksum.CRC32(buffer.AsSpan(0, 9));
            // See http://www.sunshine2k.de/coding/javascript/crc/crc_js.html
            // See http://www.lammertbies.nl/comm/info/crc-calculation.html
            Assert.Equal("CBF43926", crc.ToString("X8"));
        }

        [Fact]
        public void CRC32_sample_value_stream()
        {
            byte[] buffer = "123456789"u8.ToArray();
            using MemoryStream stream = new MemoryStream(buffer);
            
            Assert.Equal("CBF43926",
                Checksum.CRC32(stream, 0, buffer.Length).ToString("X8"));
        }

        [Fact]
        public void CRC32_sample_value_stream_buffered()
        {
            byte[] buffer = Enumerable.Range(0, 10000000).Select(i => (byte)i).ToArray();
            using MemoryStream stream = new MemoryStream(buffer);
            
            Assert.Equal("14BFFAE4",
                Checksum.CRC32(stream, 0, buffer.Length).ToString("X8"));
        }

        [Fact]
        public void CRC32_sample_value_stream_too_short_buffered()
        {
            byte[] buffer = Enumerable.Range(0, 10000000).Select(i => (byte)i).ToArray();
            using MemoryStream stream = new MemoryStream(buffer);
            
            Assert.Throws<ArgumentException>(() =>
                Checksum.CRC32(stream, 0, buffer.Length + 1).ToString("X8"));
        }

        [Fact]
        public void CRC32_sample_value_step()
        {
            byte[] buffer = "123456789"u8.ToArray();

            uint a = Checksum.Seed;
            uint b = Checksum.UpdateCRC32(buffer.AsSpan(0, 4), a);
            uint c = Checksum.UpdateCRC32(buffer.AsSpan(4), b);

            uint crc = Checksum.FinalizeCRC32(c);

            // See http://www.sunshine2k.de/coding/javascript/crc/crc_js.html
            // See http://www.lammertbies.nl/comm/info/crc-calculation.html
            Assert.Equal("CBF43926", crc.ToString("X8"));
        }
    }
}
