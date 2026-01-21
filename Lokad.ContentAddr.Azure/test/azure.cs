using Lokad.ContentAddr.Azure;
using Lokad.ContentAddr.Azure.Tests;
using Azure.Storage.Blobs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Lokad.ContentAddr.Tests
{
    public class azure : UploadFixture, IDisposable
    {
        public static readonly string Connection = File.ReadAllText("azure_connection.txt");

        protected BlobServiceClient Client;

        protected BlobContainerClient PersistContainer;

        protected BlobContainerClient StagingContainer;

        protected BlobContainerClient ArchiveContainer;

        protected BlobContainerClient DeletedContainer;

        protected string TestPrefix;

        public azure()
        {
            Client = new BlobServiceClient(Connection);
            TestPrefix = Guid.NewGuid().ToString();

            PersistContainer = Client.GetBlobContainerClient(TestPrefix + "-persist");
            PersistContainer.CreateIfNotExists();

            StagingContainer = Client.GetBlobContainerClient(TestPrefix + "-staging");
            StagingContainer.CreateIfNotExists();

            ArchiveContainer = Client.GetBlobContainerClient(TestPrefix + "-archive");
            ArchiveContainer.CreateIfNotExists();

            DeletedContainer = Client.GetBlobContainerClient(TestPrefix + "-deleted");
            DeletedContainer.CreateIfNotExists();

            var store = new AzureStore("a", PersistContainer, StagingContainer, ArchiveContainer, DeletedContainer,
                (elapsed, realm, hash, size, existed) =>
                    Console.WriteLine("[{4}] {0} {1}/{2} {3} bytes", existed ? "OLD" : "NEW", realm, hash, size, elapsed));

            WriteStore = store;
            ReadStore = store;
        }

        public void Dispose()
        {
            try { PersistContainer.DeleteIfExists(); } catch { }
            try { StagingContainer.DeleteIfExists(); } catch { }
            try { ArchiveContainer.DeleteIfExists(); } catch { }
            try { DeletedContainer.DeleteIfExists(); } catch { }
        }

        [Fact]
        public async Task small_file_azure()
        {
            var file = FakeFile(1024);
            var hash = Md5(file);
            var store = (AzureStore)WriteStore;

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", hash.ToString());

            var r = await store.WriteAsync(file, CancellationToken.None);
            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            var aBlob = await a.GetBlob();

            Assert.Equal("a/B2EA9F7FCEA831A4A63B213F41A8855B", aBlob.Name);
            Assert.True(await a.ExistsAsync(CancellationToken.None));

            Assert.Equal(1024, aBlob.GetProperties()?.Value?.ContentLength);

            var url = (await a.GetDownloadUrlAsync(
                TimeSpan.FromMinutes(20),
                "test.bin",
                "application/octet-stream",
                CancellationToken.None)).ToString();

            var prefix = PersistContainer.Uri + "/a/B2EA9F7FCEA831A4A63B213F41A8855B";
            Assert.Equal(prefix, url.Substring(0, prefix.Length));

            var suffix = "&sp=r&rscd=attachment%3Bfilename%3D\"test.bin\"&rsct=application%2Foctet-stream&sig=";
            Assert.Equal(suffix, url.Substring(url.IndexOf(suffix), suffix.Length));
        }

        [Fact()]
        public async Task archive_azure()
        {
            var file = FakeFile(1024);
            var hash = Md5(file);
            var store = (AzureStore)WriteStore;

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", hash.ToString());

            var r = await store.WriteAsync(file, CancellationToken.None);
            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            // Archive the blob
            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            await store.ArchiveBlobAsync(a);

            // Delete the blob
            var aBlob = await a.GetBlob();
            await aBlob.DeleteAsync();

            // Restore the blob from archive
            await store.TryUnArchiveBlobAsync(new Hash("B2EA9F7FCEA831A4A63B213F41A8855B"));

            // Unarchiving takes a while...
            while (true)
            {
                UnArchiveStatus status = await store.TryUnArchiveBlobAsync(new Hash("B2EA9F7FCEA831A4A63B213F41A8855B"));
                if (status == UnArchiveStatus.Done)
                    break;

                if (status == UnArchiveStatus.Rehydrating)
                    Trace.WriteLine("Blob still rehydrating");

                Thread.Sleep(TimeSpan.FromMinutes(3));
            }

            // Check blob contents are identical.
            var a2 = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            var stream = await a2.OpenAsync(default);
            foreach (var @byte in file)
                Assert.Equal(@byte, stream.ReadByte());
        }

        [Fact]
        public async Task small_preserve_etag()
        {
            var file = FakeFile(1024);
            var store = (AzureStore)WriteStore;

            var r = await store.WriteAsync(file, CancellationToken.None);
            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            var aBlob = await a.GetBlob();


            var etag = aBlob.GetProperties()?.Value?.ETag;

            await store.WriteAsync(file, CancellationToken.None);

            var b = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            var bBlob = await b.GetBlob();

            Assert.Equal(etag, bBlob.GetProperties()?.Value?.ETag);
        }

        protected static byte[] StringToBytes(string str) => Encoding.UTF8.GetBytes(str);

        protected static string BytesToString(byte[] input) => Encoding.UTF8.GetString(input);

        protected static string BytesToHash(IEnumerable<byte> hash)
        {
            var sb = new StringBuilder();
            foreach (var t in hash)
                sb.AppendFormat("{0:X2}", t);
            return sb.ToString();
        }

        protected byte[] Read(string key)
        {
            var blob = PersistContainer.GetBlobClient(key);
            using (var stream = blob.OpenRead())
            {
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        [Theory]
        [InlineData(
            "filename.tsv", // Normal ASCII filename with no special characters
            "attachment%3Bfilename%3D\"filename.tsv\"")]
        [InlineData(
            "filenäme.tsv", // Non-ASCII character in filename
            "attachment%3Bfilename%3D\"data.tsv\"%3Bfilename*%3DUTF-8%27%27filen%25c3%25a4me.tsv")]
        public async Task ContentDisposition(string filename, string attach)
        {
            var client = new BlobServiceClient(Connection);

            var persistContainer = Client.GetBlobContainerClient("contentdisposition-persist");
            var stagingContainer = Client.GetBlobContainerClient("contentdisposition-staging");
            var archiveContainer = Client.GetBlobContainerClient("contentdisposition-archive");
            var deletedContainer = Client.GetBlobContainerClient("contentdisposition-deleted");

            var store = new AzureStore("a", persistContainer, stagingContainer, archiveContainer, deletedContainer);

            var blob = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            var url = await blob.GetDownloadUrlAsync(
                new DateTime(2019, 9, 17, 21, 42, 0),
                TimeSpan.FromDays(10),
                filename,
                "text/plain",
                CancellationToken.None).ConfigureAwait(false);

            Assert.Contains(".blob.core.windows.net/contentdisposition-persist/a/B2EA9F7FCEA831A4A63B213F41A8855B", url.ToString());
            Assert.Contains(attach, url.ToString());
        }

        [Fact]
        public async Task delete_file_with_reason()
        {
            var file = FakeFile(1024);
            var hash = Md5(file);
            var store = (AzureStore)WriteStore;

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", hash.ToString());

            // write
            var r = await store.WriteAsync(file, CancellationToken.None);
            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            var aBlob = await a.GetBlob();

            Assert.Equal("a/B2EA9F7FCEA831A4A63B213F41A8855B", aBlob.Name);
            Assert.True(await a.ExistsAsync(CancellationToken.None));

            Assert.Equal(1024, aBlob.GetProperties()?.Value?.ContentLength);

            var url = (await a.GetDownloadUrlAsync(
                TimeSpan.FromMinutes(20),
                "test.bin",
                "application/octet-stream",
                CancellationToken.None)).ToString();

            var prefix = PersistContainer.Uri + "/a/B2EA9F7FCEA831A4A63B213F41A8855B";
            Assert.Equal(prefix, url.Substring(0, prefix.Length));

            var suffix = "&sp=r&rscd=attachment%3Bfilename%3D\"test.bin\"&rsct=application%2Foctet-stream&sig=";
            Assert.Equal(suffix, url.Substring(url.IndexOf(suffix), suffix.Length));

            // delete
            await store.DeleteWithReasonAsync(hash, AzureDeletedBlobInfo.Gdpr, CancellationToken.None);
            Assert.False(await a.ExistsAsync(CancellationToken.None));

            bool thrown = false;
            try
            {
                await a.GetDownloadUrlAsync(
                    TimeSpan.FromMinutes(20),
                    "test.bin",
                    "application/octet-stream",
                    CancellationToken.None);
            }
            catch (AzureDeletedBlobException e)
            {
                Assert.Equal(1024, e.AzureDeletedBlobInfo.Size);
                Assert.Equal(AzureDeletedBlobInfo.Gdpr, e.AzureDeletedBlobInfo.Reason);
                thrown = true;
            }
            Assert.True(thrown);
        }

        [Fact]
        public async Task throw_no_such_blob_exception()
        {
            var file = FakeFile(1024);
            var hash = Md5(file);
            var store = (AzureStore)WriteStore;

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", hash.ToString());

            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

            bool thrown = false;
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
