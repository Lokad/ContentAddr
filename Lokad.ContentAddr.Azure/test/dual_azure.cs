using Lokad.ContentAddr.Azure;
using Lokad.ContentAddr.Azure.Tests;
using Azure;
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
    public class dual_azure : UploadFixture, IDisposable
    {
        public static readonly string NewConnection = azure.Connection;

        public static readonly string OldConnection = File.ReadAllText("azure_dual_connection.txt");

        protected BlobServiceClient OldClient;
        protected BlobServiceClient NewClient;

        protected BlobContainerClient OldPersistContainer;
        protected BlobContainerClient NewPersistContainer;
        protected BlobContainerClient StagingContainer;
        protected BlobContainerClient ArchiveContainer;
        protected BlobContainerClient DeletedContainer;

        protected string TestPrefix;

        public dual_azure()
        {
            OldClient = new BlobServiceClient(NewConnection);
            NewClient = new BlobServiceClient(NewConnection);
            TestPrefix = Guid.NewGuid().ToString();

            OldPersistContainer = OldClient.GetBlobContainerClient(TestPrefix + "-newpersist");
            OldPersistContainer.CreateIfNotExists();

            NewPersistContainer = NewClient.GetBlobContainerClient(TestPrefix + "-oldpersist");
            NewPersistContainer.CreateIfNotExists();

            StagingContainer = NewClient.GetBlobContainerClient(TestPrefix + "-staging");
            StagingContainer.CreateIfNotExists();

            ArchiveContainer = NewClient.GetBlobContainerClient(TestPrefix + "-archive");
            ArchiveContainer.CreateIfNotExists();

            DeletedContainer = NewClient.GetBlobContainerClient(TestPrefix + "-deleted");
            DeletedContainer.CreateIfNotExists();

            var store = new DualAzureStore("a", OldPersistContainer, NewPersistContainer, StagingContainer, ArchiveContainer, DeletedContainer);
            WriteStore = store;
            ReadStore = store;
        }

        public void Dispose()
        {
            try { NewPersistContainer.DeleteIfExists(); } catch { }
            try { OldPersistContainer.DeleteIfExists(); } catch { }
            try { StagingContainer.DeleteIfExists(); } catch { }
            try { DeletedContainer.DeleteIfExists(); } catch { }
        }

        [Fact]
        public async Task small_file_azure()
        {
            var file = FakeFile(1024);
            var hash = Md5(file);
            var store = (DualAzureStore)WriteStore;

            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", hash.ToString());

            var r = await store.WriteAsync(file, CancellationToken.None);
            Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
            Assert.Equal(1024, r.Size);

            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            var aNewBlob = await a.GetBlob();

            Assert.Equal("a/B2EA9F7FCEA831A4A63B213F41A8855B", aNewBlob.Name);
            Assert.True(await a.ExistsAsync(CancellationToken.None));

            Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

            var url = (await a.GetDownloadUrlAsync(
                TimeSpan.FromMinutes(20),
                "test.bin",
                "application/octet-stream",
                CancellationToken.None)).ToString();

            var prefix = NewPersistContainer.Uri + "/a/B2EA9F7FCEA831A4A63B213F41A8855B";
            Assert.Equal(prefix, url.Substring(0, prefix.Length));

            var suffix = "&sp=r&rscd=attachment%3Bfilename%3D\"test.bin\"&rsct=application%2Foctet-stream&sig=";
            Assert.Equal(suffix, url.Substring(url.IndexOf(suffix), suffix.Length));
        }

        [Fact]
        public async Task small_preserve_etag()
        {
            var file = FakeFile(1024);
            var store = (DualAzureStore)WriteStore;

            var r = await store.WriteAsync(file, CancellationToken.None);
            var a = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            var aNewBlob = await a.GetBlob();

            var etag = aNewBlob.GetProperties()?.Value?.ETag;

            await store.WriteAsync(file, CancellationToken.None);

            var b = store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];
            var bNewBlob = await b.GetBlob();

            Assert.Equal(etag, bNewBlob.GetProperties()?.Value?.ETag);
        }

        [Fact]
        public async Task small_file_to_move()
        {
            var file = FakeFile(2048);
            var hash = Md5(file);
            var store = new DualAzureStore("b", OldPersistContainer, NewPersistContainer, StagingContainer, ArchiveContainer, DeletedContainer);
            var reverseStore = new DualAzureStore("b", NewPersistContainer, OldPersistContainer, StagingContainer, ArchiveContainer, DeletedContainer);
            var newBlobStore = new AzureStore("b", NewPersistContainer, StagingContainer, ArchiveContainer, DeletedContainer);

            await reverseStore.WriteAsync(file, CancellationToken.None);
            var a = store[hash];
            var aNewBlob = await newBlobStore[hash].GetBlob();
            var aOldBlob = await reverseStore[hash].GetBlob();

            Assert.True(await aOldBlob.ExistsAsync(CancellationToken.None));
            Assert.False(await aNewBlob.ExistsAsync(CancellationToken.None));

            Assert.Equal(2048, await a.GetSizeAsync(CancellationToken.None));
            Stopwatch sw = new Stopwatch();

            while (sw.Elapsed < TimeSpan.FromSeconds(5) &&
                !(await aNewBlob.ExistsAsync(CancellationToken.None))) ;

            
            Assert.Equal(2048, aNewBlob.GetProperties()?.Value?.ContentLength);
        }

        [Fact]
        public void dual_config_to_parse()
        {
            var dualConfig = OldConnection + "||" + NewConnection;

            IAzureStoreFactory dualFactory = AzureStoreFactory.ParseConfig(dualConfig);
            Assert.Equal("Lokad.ContentAddr.Azure.DualAzureStoreFactory", dualFactory.GetType().ToString());
            IAzureStoreFactory singleFactory = AzureStoreFactory.ParseConfig(NewConnection);
            Assert.Equal("Lokad.ContentAddr.Azure.AzureStoreFactory", singleFactory.GetType().ToString());
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
            var blob = NewPersistContainer.GetBlobClient(key);
            using (var stream = blob.OpenRead())
            {
                var ms = new MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
