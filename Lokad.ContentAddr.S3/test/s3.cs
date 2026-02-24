using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Lokad.ContentAddr.S3.Tests
{
    public class s3 : UploadFixture, IDisposable
    {
        public static readonly string Config = File.ReadAllText("s3_connection.txt");

        private readonly S3StoreFactory _factory;
        private readonly ITestOutputHelper _output;

        public s3(ITestOutputHelper output)
        {
            _output = output;
            _factory = new S3StoreFactory(Config, readOnly: false, testPrefix: Guid.NewGuid().ToString("N"));

            var store = (S3Store)_factory.ForAccount(1);
            WriteStore = store;
            ReadStore = store;
        }

        public void Dispose()
        {
            try { _factory.Delete(); } catch { }
        }

        [Fact]
        public async Task small_file_s3()
        {
            var file = FakeFile(1024);
            var hash = Md5(file);
            var store = (S3Store)WriteStore;

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", hash.ToString());

            var r = await store.WriteAsync(file, CancellationToken.None);
            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

            Assert.True(await a.ExistsAsync(CancellationToken.None));
            Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

            var url = (await a.GetDownloadUrlAsync(
                TimeSpan.FromMinutes(20),
                "test.bin",
                "application/octet-stream",
                CancellationToken.None)).ToString();

            Assert.Contains(a.Key, url);
            Assert.Contains("response-content-disposition=attachment%3Bfilename%3D\"test.bin\"", url);
            Assert.Contains("response-content-type=application%2Foctet-stream", url);
        }

        [Fact]
        public async Task delete_file_with_reason()
        {
            var file = FakeFile(1024);
            var hash = Md5(file);
            var store = (S3Store)WriteStore;

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", hash.ToString());

            var r = await store.WriteAsync(file, CancellationToken.None);
            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            Assert.True(await a.ExistsAsync(CancellationToken.None));

            await store.DeleteWithReasonAsync(hash, S3DeletedBlobInfo.Gdpr, CancellationToken.None);
            Assert.False(await a.ExistsAsync(CancellationToken.None));

            var thrown = false;
            try
            {
                await a.GetDownloadUrlAsync(
                    TimeSpan.FromMinutes(20),
                    "test.bin",
                    "application/octet-stream",
                    CancellationToken.None);
            }
            catch (S3DeletedBlobException e)
            {
                Assert.Equal(1024, e.S3DeletedBlobInfo.Size);
                Assert.Equal(S3DeletedBlobInfo.Gdpr, e.S3DeletedBlobInfo.Reason);
                thrown = true;
            }
            Assert.True(thrown);
        }

        [Fact]
        public async Task throw_no_such_blob_exception()
        {
            var store = (S3Store)WriteStore;
            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

            var thrown = false;
            try
            {
                await a.GetDownloadUrlAsync(
                    TimeSpan.FromMinutes(20),
                    "test.bin",
                    "application/octet-stream",
                    CancellationToken.None);
            }
            catch (NoSuchBlobException)
            {
                thrown = true;
            }
            Assert.True(thrown);
        }

        [Fact(Skip = "Very long")]
        public async Task upload_6gib_file()
        {
            const long gib = 1024L * 1024 * 1024;
            const int chunkSize = 5 * 1024 * 1024;
            var totalSize = 6L * gib;

            var store = (S3Store)WriteStore;
            var nextInt = 0;

            WrittenBlob result;
            using (var writer = store.StartWriting())
            {
                long written = 0;
                var chunkIndex = 0;
                while (written < totalSize)
                {
                    var count = (int)Math.Min(chunkSize, totalSize - written);
                    var chunk = new byte[count];
                    FillChunkWithConsecutiveInt32s(chunk, ref nextInt);

                    await writer.WriteAsync(chunk, 0, count, CancellationToken.None);
                    written += count;
                    chunkIndex++;
                    _output.WriteLine($"chunk {chunkIndex}: +{count} bytes, total {written}/{totalSize}");
                }

                result = await writer.CommitAsync(CancellationToken.None);
            }

            Assert.Equal(totalSize, result.Size);
            //Assert.Equal(uploadedHash, result.Hash);
            
            var blob = store[result.Hash];
            Assert.True(await blob.ExistsAsync(CancellationToken.None));
            Assert.Equal(totalSize, await blob.GetSizeAsync(CancellationToken.None));

            Hash downloadedHash;
            using (var stream = await blob.OpenAsync(CancellationToken.None))
            {
                var buffer = new byte[8 * 1024 * 1024];
                using var downloadedMd5 = MD5.Create();

                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None)) > 0)
                    downloadedMd5.TransformBlock(buffer, 0, read, buffer, 0);

                downloadedMd5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                downloadedHash = new Hash(downloadedMd5.Hash);
            }

            Assert.Equal(result.Hash, downloadedHash);
        }

        private static void FillChunkWithConsecutiveInt32s(byte[] chunk, ref int nextInt)
        {
            for (var offset = 0; offset < chunk.Length; offset += sizeof(int))
            {
                BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(offset, sizeof(int)), nextInt);
                nextInt++;
            }
        }
    }
}
