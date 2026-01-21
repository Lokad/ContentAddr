using Lokad.ContentAddr.S3;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Lokad.ContentAddr.S3.Tests
{
    public sealed class s3_read_stream : IDisposable
    {
        public static readonly string Config = File.ReadAllText("s3_connection.txt");
        public static readonly byte[] Data;
        private const int MB = 1024 * 1024;

        private readonly S3StoreFactory _factory;
        private readonly S3Store _store;
        private Task<IS3ReadBlobRef> _blobTask;

        static s3_read_stream()
        {
            var data = new byte[8 * MB];
            for (var i = 0; i < data.Length; i += 2)
            {
                data[i] = (byte)(i / 512);
                data[i + 1] = (byte)(i / 2);
            }

            Data = data;
        }

        public s3_read_stream()
        {
            _factory = new S3StoreFactory(Config, readOnly: false, testPrefix: Guid.NewGuid().ToString("N"));
            _store = (S3Store)_factory.ForAccount(1);
        }

        public void Dispose()
        {
            try { _factory.Delete(); } catch { }
        }

        private Task<IS3ReadBlobRef> Blob()
        {
            if (_blobTask == null)
                _blobTask = CreateBlobAsync();

            return _blobTask;
        }

        private async Task<IS3ReadBlobRef> CreateBlobAsync()
        {
            var written = await _store.WriteAsync(Data, CancellationToken.None).ConfigureAwait(false);
            return _store[written.Hash];
        }

        [Fact]
        public async Task empty()
        {
            var blob = await Blob().ConfigureAwait(false);
            var stream = new S3ReadStream(_factory.Client, blob.Bucket, blob.Key, 0);

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
            var blob = await Blob().ConfigureAwait(false);
            var stream = new S3ReadStream(_factory.Client, blob.Bucket, blob.Key, Data.Length);

            foreach (var @byte in Data)
                Assert.Equal(@byte, stream.ReadByte());

            Assert.Equal(-1, stream.ReadByte());
        }

        [Fact]
        public async Task read_byte_seek_far()
        {
            var blob = await Blob().ConfigureAwait(false);
            var stream = new S3ReadStream(_factory.Client, blob.Bucket, blob.Key, Data.Length);
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
            var blob = await Blob().ConfigureAwait(false);
            var stream = new S3ReadStream(_factory.Client, blob.Bucket, blob.Key, Data.Length);
            const int offset = 5_000_000;

            stream.Seek(offset, SeekOrigin.Begin);
            foreach (var @byte in Data.Skip(offset))
                Assert.Equal(@byte, stream.ReadByte());

            Assert.Equal(-1, stream.ReadByte());
        }

        [Fact]
        public async Task read_byte_seek_near()
        {
            var blob = await Blob().ConfigureAwait(false);
            var stream = new S3ReadStream(_factory.Client, blob.Bucket, blob.Key, Data.Length);
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
            var blob = await Blob().ConfigureAwait(false);
            var stream = new S3ReadStream(_factory.Client, blob.Bucket, blob.Key, Data.Length);
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
            var blob = await Blob().ConfigureAwait(false);
            var stream = new S3ReadStream(_factory.Client, blob.Bucket, blob.Key, Data.Length);
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
            var blob = await Blob().ConfigureAwait(false);
            var stream = new S3ReadStream(_factory.Client, blob.Bucket, blob.Key, Data.Length);
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
            var blob = await Blob().ConfigureAwait(false);
            var stream = new S3ReadStream(_factory.Client, blob.Bucket, blob.Key, Data.Length);
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
