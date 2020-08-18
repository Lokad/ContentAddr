using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Lokad.ContentAddr.Tests
{
    public abstract class UploadFixture
    {
        protected IStore<IReadBlobRef> Store { get; set; }

        [Fact]
        public async Task empty()
        {
            var r = await Store.WriteAsync(new byte[0], CancellationToken.None);
            Assert.Equal("D41D8CD98F00B204E9800998ECF8427E", r.Hash.ToString());
            Assert.Equal(0, r.Size);

            var a = Store[ new Hash("D41D8CD98F00B204E9800998ECF8427E")];

            Assert.True(await a.ExistsAsync(CancellationToken.None));
            Assert.Equal(0, await a.GetSizeAsync(CancellationToken.None));

            using (var s = await a.OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[10];
                Assert.Equal(0, await s.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
            }
        }

        [Fact]
        public async Task small_file()
        {
            var file = FakeFile(1024);
            var hash = Md5(file);

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", hash.ToString());

            var r = await Store.WriteAsync(file, CancellationToken.None);
            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            var a = Store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

            Assert.True(await a.ExistsAsync(CancellationToken.None));
            Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

            using (var s = await a.OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[10];
                Assert.Equal(10, await s.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
                Assert.Equal(new byte[] {0,1,2,3,4,5,6,7,8,9}, bytes);
            }
        }

        [Fact]
        public async Task small_file_sub()
        {
            var file = FakeFile(1024 * 2);
        
            var r = await Store.WriteAsync(file, 256, 1024, CancellationToken.None);
            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            var a = Store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

            Assert.True(await a.ExistsAsync(CancellationToken.None));
            Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

            using (var s = await a.OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[10];
                Assert.Equal(10, await s.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, bytes);
            }
        }
        
        [Fact]
        public async Task small_file_split()
        {
            var file = FakeFile(1024);
            
            WrittenBlob r;
            using (var w = Store.StartWriting())
            {
                for (var i = 0; i < file.Length; i += 256)
                    await w.WriteAsync(file, i, 256, CancellationToken.None);

                r = await w.CommitAsync(CancellationToken.None);
            }

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            var a = Store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

            Assert.True(await a.ExistsAsync(CancellationToken.None));
            Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

            using (var s = await a.OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[10];
                Assert.Equal(10, await s.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, bytes);
            }
        }

        [Fact]
        public async Task end_of_stream()
        {
            var file = FakeFile(1024);

            WrittenBlob r;
            using (var w = Store.StartWriting())
            {
                var pos = 0;
                await w.WriteAsync((bytes, offset, count, cancel) =>
                {
                    if (pos == file.Length)
                    {
                        pos++;
                        return Task.FromResult(0);
                    }

                    if (pos > file.Length)
                        Assert.True(false, "Read function called after it had returned zero.");

                    var read = Math.Min(Math.Min(file.Length - pos, 17), count);

                    for (var i = 0; i < read; ++i)
                        bytes[offset + i] = file[pos + i];

                    pos += read;

                    return Task.FromResult(read);

                }, null, CancellationToken.None);

                r = await w.CommitAsync(CancellationToken.None);
            }

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            var a = Store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

            Assert.True(await a.ExistsAsync(CancellationToken.None));
            Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

            using (var s = await a.OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[10];
                Assert.Equal(10, await s.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, bytes);
            }
        }

        [Fact]
        public async Task large_file()
        {
            // Exceeds the 4MB block size
            var file = FakeFile(1024 * 1024 * 9);
            var hash = Md5(file);

            Assert.Equal("2A43A3F3D5B1A13F0B4C3369040D0919", hash.ToString());

            var r = await Store.WriteAsync(file, CancellationToken.None);
            Assert.Equal("2A43A3F3D5B1A13F0B4C3369040D0919", r.Hash.ToString());
            Assert.Equal(1024 * 1024 * 9, r.Size);

            var a = Store[new Hash("2A43A3F3D5B1A13F0B4C3369040D0919")];

            Assert.True(await a.ExistsAsync(CancellationToken.None));
            Assert.Equal(1024 * 1024 * 9, await a.GetSizeAsync(CancellationToken.None));

            using (var s = await a.OpenAsync(CancellationToken.None))
            {
                var bytes = new byte[10];
                Assert.Equal(10, await s.ReadAsync(bytes, 0, bytes.Length, CancellationToken.None));
                Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, bytes);
            }

            using (var s = await a.OpenAsync(CancellationToken.None))
                Assert.Equal(r.Hash, new Hash(MD5.Create().ComputeHash(s)));
        }
        
        /// <summary> Misleading name. It does *not* convert a long to a byte[] </summary>
        protected static byte[] FakeFile(long n)
        {
            var b = new byte[n];
            for (var i = 0; i < n; ++i) b[i] = (byte)i;
            return b;
        }

        protected static Hash Md5(byte[] input) => new Hash(MD5.Create().ComputeHash(input));
    }
}
