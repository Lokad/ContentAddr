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
using Azure.Storage.Sas;

namespace Lokad.ContentAddr.Tests
{
    public class azure_ro : UploadFixture, IDisposable
    {
        public static readonly string Connection = File.ReadAllText("azure_connection.txt");

        protected BlobServiceClient Client;

        protected BlobContainerClient PersistContainer;

        protected BlobContainerClient StagingContainer;

        protected BlobContainerClient ArchiveContainer;

        protected BlobContainerClient DeletedContainer;

        protected string TestPrefix;

        public azure_ro()
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

            WriteStore = new AzureStore("a", PersistContainer, StagingContainer, ArchiveContainer, DeletedContainer,
                (elapsed, realm, hash, size, existed) =>
                    Console.WriteLine("[{4}] {0} {1}/{2} {3} bytes", existed ? "OLD" : "NEW", realm, hash, size, elapsed));

            var persistSas = PersistContainer.GenerateSasUri(
                new BlobSasBuilder(BlobContainerSasPermissions.Read,
                new DateTimeOffset(DateTime.UtcNow.AddDays(1))));

            var newPersistContainer = new BlobContainerClient(persistSas);

            ReadStore = new AzureReadOnlyStore("a", newPersistContainer, DeletedContainer);
        }

        public void Dispose()
        {
            try { PersistContainer.DeleteIfExists(); } catch { }
            try { StagingContainer.DeleteIfExists(); } catch { }
            try { ArchiveContainer.DeleteIfExists(); } catch { }
            try { DeletedContainer.DeleteIfExists(); } catch { }
        }
    }
}
