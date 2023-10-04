using Lokad.ContentAddr.Mapped;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Lokad.ContentAddr.Tests
{    
    public class mapped : UploadFixture, IDisposable
    {
        private readonly string _path;
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedStore _store;

        public mapped()
        {
            _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            _mmf = MemoryMappedFile.CreateFromFile(_path, FileMode.Create, null, 10 << 20 /* 10 MB */);
            Store = _store = new MemoryMappedStore(0, _mmf, false);
        }

        public void Dispose()
        {
            _store.Dispose();
            File.Delete(_path);
        }

        [Fact]
        public async Task truncate()
        {
            var r1 = await Store.WriteAsync(new byte[] { 0x01, 0x02 }, default);
            var r2 = await Store.WriteAsync(new byte[] { 0x01, 0x02, 0x03 }, default);
            var size = _store.Size;
            var r3 = await Store.WriteAsync(new byte[] { 0x01, 0x02, 0x03, 0x04 }, default);

            _store.Truncate(size);

            using (var s1 = await Store[r1.Hash].OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[6];
                Assert.Equal(2, await s1.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None)); 
                Assert.Equal(new byte[] { 0x01, 0x02, 0, 0, 0, 0 }, bytes);
            }

            using (var s2 = await Store[r2.Hash].OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[6];
                Assert.Equal(3, await s2.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
                Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0, 0, 0 }, bytes);
            }

            Assert.False(await Store[r3.Hash].ExistsAsync(default));
        }

        [Fact]
        public async Task share()
        {
            var r1 = await Store.WriteAsync(new byte[] { 0x01, 0x02 }, default);
            var r2 = await Store.WriteAsync(new byte[] { 0x01, 0x02, 0x03 }, default);

            using var store2 = new MemoryMappedStore(0, _mmf, true);

            var r3 = await store2.WriteAsync(new byte[] { 0x01, 0x02, 0x03, 0x04 }, default);

            using (var s1 = await store2[r1.Hash].OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[6];
                Assert.Equal(2, await s1.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
                Assert.Equal(new byte[] { 0x01, 0x02, 0, 0, 0, 0 }, bytes);
            }

            using (var s2 = await store2[r2.Hash].OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[6];
                Assert.Equal(3, await s2.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
                Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0, 0, 0 }, bytes);
            }

            using (var s3 = await Store[r3.Hash].OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[6];
                Assert.Equal(4, await s3.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
                Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0, 0 }, bytes);
            }
        }
    }
}
