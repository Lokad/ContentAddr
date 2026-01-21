using Lokad.ContentAddr.Azure;
using Azure;
using Azure.Storage.Blobs;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Azure.Storage.Blobs.Models;
using System.Collections.Generic;
using Xunit.Sdk;

namespace Lokad.ContentAddr.Tests.Azure
{
    public sealed class azure_read_stream
    {
        public static readonly string Connection = azure.Connection;
        public static readonly byte[] Data;
        private const int MB = 1024 * 1024;

        static azure_read_stream()
        {
            var data = new byte[8 * MB];
            for (var i = 0; i < data.Length; i += 2)
            {
                data[i] = (byte)(i / 512);
                data[i + 1] = (byte)(i / 2);
            }

            Data = data;
        }

        private async Task<BlobClient> Blob()
        {
            var client = new BlobServiceClient(Connection);
            var container = client.GetBlobContainerClient("azure-read-stream");
            await container.CreateIfNotExistsAsync().ConfigureAwait(false);

            var blob = container.GetBlobClient("large");
            if (!await blob.ExistsAsync().ConfigureAwait(false))
                await blob.UploadAsync(new BinaryData(Data)).ConfigureAwait(false);

            return blob;
        }

        [Fact]
        public async Task empty()
        {
            var stream = new AzureReadStream(await Blob(), 0);

            Assert.Equal(0, stream.Position);
            Assert.Equal(0, stream.Seek(0, SeekOrigin.Current));
            Assert.Equal(-1, stream.ReadByte());

            var buf = new byte[10];
            Assert.Equal(0, stream.Read(buf, 0, 10));
            Assert.Equal(0, await stream.ReadAsync(buf, 0, 10).ConfigureAwait(false));
        }

        [Fact]
        public async Task read_byte()
        {
            var stream = new AzureReadStream(await Blob(), Data.Length);

            foreach (var @byte in Data)
                Assert.Equal(@byte, stream.ReadByte());

            Assert.Equal(-1, stream.ReadByte());
        }

        [Fact]
        public async Task read_byte_seek_far()
        {
            var stream = new AzureReadStream(await Blob(), Data.Length);
            stream.ReadByte();

            const int offset = 5_000_000;

            stream.Seek(offset, SeekOrigin.Begin);
            foreach (var @byte in Data.Skip(offset))
                Assert.Equal(@byte, stream.ReadByte());

            Assert.Equal(-1, stream.ReadByte());
        }

        [Fact]
        public async Task read_byte_initial_seek_far()
        {
            var stream = new AzureReadStream(await Blob(), Data.Length);
            const int offset = 5_000_000;

            stream.Seek(offset, SeekOrigin.Begin);
            foreach (var @byte in Data.Skip(offset))
                Assert.Equal(@byte, stream.ReadByte());

            Assert.Equal(-1, stream.ReadByte());
        }

        [Fact]
        public async Task read_byte_seek_near()
        {
            var stream = new AzureReadStream(await Blob(), Data.Length);
            stream.ReadByte();

            const int offset = 1_000_000;

            stream.Seek(offset, SeekOrigin.Begin);
            foreach (var @byte in Data.Skip(offset))
                Assert.Equal(@byte, stream.ReadByte());

            Assert.Equal(-1, stream.ReadByte());
        }

        [Fact]
        public async Task read_byte_seek_far_back()
        {
            var stream = new AzureReadStream(await Blob(), Data.Length);
            const int offset = 5_000_000;

            stream.Seek(offset, SeekOrigin.Begin);
            stream.ReadByte();

            stream.Seek(0, SeekOrigin.Begin);

            foreach (var @byte in Data)
                Assert.Equal(@byte, stream.ReadByte());

            Assert.Equal(-1, stream.ReadByte());
        }

        [Fact]
        public async Task read_byte_seek_near_back()
        {
            var stream = new AzureReadStream(await Blob(), Data.Length);
            stream.ReadByte();
            const int offset = 1_000_000;

            stream.Seek(offset, SeekOrigin.Begin);
            stream.ReadByte();

            stream.Seek(0, SeekOrigin.Begin);

            foreach (var @byte in Data)
                Assert.Equal(@byte, stream.ReadByte());

            Assert.Equal(-1, stream.ReadByte());
        }

        [Theory]
        [InlineData(0, 0, 5 * MB)]
        [InlineData(MB, 0, 5 * MB)]
        [InlineData(0, MB, 5 * MB)]
        [InlineData(MB, MB, 5 * MB)]
        [InlineData(0, 0, 3 * MB)]
        [InlineData(MB, 0, 3 * MB)]
        [InlineData(0, MB, 3 * MB)]
        [InlineData(MB, MB, 3 * MB)]
        [InlineData(2 * MB, 0, 3 * MB)]
        [InlineData(4 * MB, 0, 3 * MB)]
        public async Task read(int seek, int offset, int count)
        {
            var stream = new AzureReadStream(await Blob(), Data.Length);
            var buf = new byte[offset + count];
            if (seek != 0)
            {
                stream.ReadByte();
                stream.Seek(seek, SeekOrigin.Begin);
            }

            var realCount = Math.Min(count, Data.Length - seek);
            Assert.Equal(realCount, stream.Read(buf, offset, count));
            Assert.Equal(Data.Skip(seek).Take(realCount), buf.Skip(offset).Take(realCount));
            Assert.Equal(seek + realCount, stream.Position);
        }

        [Theory]
        [InlineData(0, 0, 5 * MB)]
        [InlineData(MB, 0, 5 * MB)]
        [InlineData(0, MB, 5 * MB)]
        [InlineData(MB, MB, 5 * MB)]
        [InlineData(0, 0, 3 * MB)]
        [InlineData(MB, 0, 3 * MB)]
        [InlineData(0, MB, 3 * MB)]
        [InlineData(MB, MB, 3 * MB)]
        [InlineData(2 * MB, 0, 3 * MB)]
        [InlineData(4 * MB, 0, 3 * MB)]
        public async Task read_async(int seek, int offset, int count)
        {
            var stream = new AzureReadStream(await Blob(), Data.Length);
            var buf = new byte[offset + count];
            if (seek != 0)
            {
                stream.ReadByte();
                stream.Seek(seek, SeekOrigin.Begin);
            }

            var realCount = Math.Min(count, Data.Length - seek);
            Assert.Equal(realCount, await stream.ReadAsync(buf, offset, count));
            Assert.Equal(Data.Skip(seek).Take(realCount), buf.Skip(offset).Take(realCount));
            Assert.Equal(seek + realCount, stream.Position);
        }
    }
}

