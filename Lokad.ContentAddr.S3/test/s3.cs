using Lokad.ContentAddr.S3;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Lokad.ContentAddr.S3.Tests
{
    public class s3 : UploadFixture, IDisposable
    {
        public static readonly string Config = File.ReadAllText("s3_connection.txt");

        private readonly S3StoreFactory _factory;

        public s3()
        {
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
    }
}
