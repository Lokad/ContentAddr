using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    public enum UnArchiveStatus
    {
        DoesNotExist,
        Rehydrating,
        Done
    }
    /// <summary> Persistent content-addressable store backed by Azure blobs. </summary>
    /// <see cref="AzureReadOnlyStore"/>
    /// <remarks>
    ///     Supports uploading blobs, as well as committing blobs uploaded to a staging
    ///     container.
    /// </remarks>
    public sealed class AzureStore : AzureReadOnlyStore, IAzureStore
    {
        /// <summary> Called when a new blob is committed. </summary>
        private readonly AzureWriter.OnCommit _onCommit;

        /// <summary> The container where temporary blobs are staged for a short while. </summary>
        private BlobContainerClient Staging { get; }

        /// <summary> The container where archived blobs are stored. </summary>
        private BlobContainerClient Archive { get; }

        /// <param name="realm"> <see cref="AzureReadOnlyStore"/> </param>
        /// <param name="persistent"> 
        ///     Blobs are stored here, named according to 
        ///     <see cref="AzureReadOnlyStore.AzureBlobName"/>.
        /// </param>
        /// <param name="staging"> Temporary blobs are stored here. </param>
        /// <param name="archive"> Archived blobs are stored here. </param>
        /// <param name="onCommit"> Called when a blob is committed. </param>
        public AzureStore(
            string realm,
            BlobContainerClient persistent,
            BlobContainerClient staging,
            BlobContainerClient archive,
            BlobContainerClient deleted,
            AzureWriter.OnCommit onCommit = null) : base(realm, persistent, deleted)
        {
            _onCommit = onCommit;
            Staging = staging;
            Archive = archive;
        }

        /// <see cref="IStore{TBlobRef}.StartWriting"/>
        public StoreWriter StartWriting() =>
            new AzureWriter(Realm, Persistent, TempBlob(), _onCommit);

        /// <summary> A reference to a temporary blob in the staging container. </summary>
        private BlockBlobClient TempBlob() =>
            Staging.GetBlockBlobClient(
                DateTime.UtcNow.ToString("yyyy-MM-dd") + "/" + Realm + "/" + Guid.NewGuid());

        /// <summary> Get the URL of a temporary blob where data can be uploaded. </summary>
        /// <remarks> 
        ///     Commit blob with <see cref="CommitTemporaryBlob"/>.
        /// 
        ///     The name should be a valid Azure Blob name, but its contents are not important
        ///     (although it is recommended that a "date/realm/guid" format is used to make
        ///     cleanup easier, to prevent cross-realm contamination, and to avoid collisions).
        /// </remarks>
        public Uri GetSignedUploadUrl(string name, TimeSpan life)
        {
            var blob = Staging.GetBlobClient(name);
            var url = blob.GenerateSasUri(BlobSasPermissions.Write | BlobSasPermissions.Delete,
                expiresOn: new DateTimeOffset(DateTime.UtcNow + life));

            return url;
        }

        /// <summary> Compress a blob into the archive container and set its tier to "archive" in Azure. </summary>
        /// <param name="blob"> The blob to be archived. </param>
        public async Task ArchiveBlobAsync(IAzureReadBlobRef blob, CancellationToken cancel = default)
        {
            var aBlob = await blob.GetBlob();
            var destinationBlobClient = Archive.GetBlobClient(aBlob.Name);

            if (await destinationBlobClient.ExistsAsync(cancel)) return;

            using (var azureWriteStream = await destinationBlobClient.OpenWriteAsync(true, new BlobOpenWriteOptions
            {
                BufferSize = 4 * 1024 * 1024
            }, cancellationToken: cancel))
            {
                using (var gzStream = new GZipStream(azureWriteStream, CompressionMode.Compress))
                {
                    using (var azureReadStream = await blob.OpenAsync(cancel))
                    {
                        await azureReadStream.CopyToAsync(gzStream);
                    }
                }
            }

            await destinationBlobClient.SetAccessTierAsync(AccessTier.Archive, cancellationToken: cancel);
        }

        /// <summary> Return true if an archived copy of the blob exists in the archive container. </summary>
        public async Task<bool> BlobHasArchivedCopyAsync(IAzureReadBlobRef blob, CancellationToken cancel = default)
        {
            var sourceBlob = await blob.GetBlob();
            var archivedBlob = Archive.GetBlobClient(sourceBlob.Name);
            return await archivedBlob.ExistsAsync(cancel);
        }

        /// <summary>
        ///     UnArchive a blob. It's a long process split into several steps. First step is to move
        ///     the archived blob to the staging container and ask for its rehydratation.
        /// </summary>
        /// <remarks>
        ///     Rehydratation can take several hours, so come later and call this function again
        ///     to perform decompression into the persistent container.
        /// </remarks>
        /// <param name="hash"> The hash of the archived blob to be unarchived. </param>
        public async Task<UnArchiveStatus> TryUnArchiveBlobAsync(Hash hash, CancellationToken cancel = default)
        {
            var blobName = AzureBlobName(Realm, hash);

            // we check if UnArchive was already done successfully : if blob exists in Persistent
            var pBlob = Persistent.GetBlobClient(blobName);
            if (await pBlob.ExistsAsync(cancel))
                return UnArchiveStatus.Done;

            // We check if Blob is already copied in Staging
            var sBlob = Staging.GetBlobClient(blobName);
            if (await sBlob.ExistsAsync(cancel))
            {
                // We check which status it has
                if (sBlob.GetProperties()?.Value?.AccessTier != AccessTier.Hot.ToString())
                    return UnArchiveStatus.Rehydrating;

                // decompressing compressed blob into Persistent
                using (var azureReadStream = await sBlob.OpenReadAsync(cancellationToken: cancel))
                {
                    using (var gzStream = new GZipStream(azureReadStream, CompressionMode.Decompress))
                    {
                        var wBlob = await this.WriteAsync(gzStream, default);
                        if (!wBlob.Hash.Equals(hash))
                            throw new InvalidOperationException($"Unarchiving {sBlob.Name} produces incorrect hash {hash}");
                    }
                }

                return UnArchiveStatus.Done;
            }

            // we check if the archived blob exists
            var aBlob = Archive.GetBlobClient(blobName);
            if (!(await aBlob.ExistsAsync(cancel)))
                return UnArchiveStatus.DoesNotExist;

            // if blob not already copied in Staging,
            // copy it by using a newer API version that
            // deal with unarchiving at the same time

            await sBlob.StartCopyFromUriAsync(aBlob.Uri,
                new BlobCopyFromUriOptions
                {
                    RehydratePriority = RehydratePriority.High,
                    AccessTier = AccessTier.Hot
                }
                , cancellationToken: cancel);


            return UnArchiveStatus.Rehydrating;
        }

        /// <summary> Commit a blob from staging to the persistent store. </summary>
        /// <remarks>
        ///     Computes the hash of the blob before committing it.
        /// </remarks>
        /// <param name="name"> The full name of the temporary blob. </param>
        /// <param name="cancel"> Cancellation token. </param>
        public async Task<IAzureReadBlobRef> CommitTemporaryBlob(string name, CancellationToken cancel)
        {
            var sw = Stopwatch.StartNew();

            var temporary = Staging.GetBlockBlobClient(name);
            if (!await temporary.ExistsAsync(cancel).ConfigureAwait(false))
                throw new CommitBlobException(Realm, name, "temporary blob does not exist.");

            var md5 = MD5.Create();

            // We use buffered async reading, so determine a good buffer size.
            var bufferSize = 4 * 1024 * 1024;

            long? blobLength = temporary.GetProperties()?.Value?.ContentLength;
            if (blobLength < bufferSize)
                bufferSize = (int)blobLength.Value;

            var buffer = new byte[bufferSize];

            int nbRead = 1;
            long position = 0;

            using (var stream = await temporary.OpenReadAsync(cancellationToken: cancel).ConfigureAwait(false))
            {
                int read = 0;
                do
                {
                    read = await stream.ReadAsync(buffer, 0, bufferSize, cancel)
                        .ConfigureAwait(false);

                    position += read;
                    nbRead++;

                    md5.TransformBlock(buffer, 0, read, buffer, 0);

                } while (read > 0);
            }

            md5.TransformFinalBlock(buffer, 0, 0);

            var hash = new Hash(md5.Hash);

            var final = Persistent.GetBlobClient(AzureBlobName(Realm, hash));

            try
            {
                var exists = await AzureRetry.OrFalse(async () => await final.ExistsAsync(cancellationToken: cancel))
                    .ConfigureAwait(false);

                if (!exists)
                {
                    await CopyToPersistent(temporary, final, cancel).ConfigureAwait(false);
                }

                _onCommit?.Invoke(sw.Elapsed, Realm, hash, final.GetProperties()?.Value?.ContentLength ?? 0, exists);
            }
            finally
            {
                // Always delete the blob.
                DeleteBlob(temporary, TimeSpan.FromMinutes(10));
            }

            return new AzureBlobRef(Realm, hash, final, Deleted);
        }

        /// <summary> Delete a block after a short wait. </summary>
        /// <remarks> This schedules the deletion but does not wait for it. </remarks>
        public static void DeleteBlob(BlockBlobClient temporary, TimeSpan wait)
        {
            // After a short while, delete the staging blob. Don't do it immediately, just
            // in case another thread (or server) is currently touching it as well.
            Task.Delay(wait).ContinueWith(_ => temporary.DeleteIfExistsAsync());
        }

        /// <summary> Copy a temporary blob to a persistent final blob. </summary>
        /// <remarks>
        ///     The task completes when the blob has been copied, or the copy has
        ///     failed. 
        /// </remarks>
        public static async Task CopyToPersistent(
            BlockBlobClient temporary,
            BlobClient final,
            CancellationToken cancel)
        {
            // Copy the blob over.
            await AzureRetry.Do(
                async c => {
                    try
                    {
                        await final.StartCopyFromUriAsync(
                            temporary.Uri,
                            destinationConditions: new BlobRequestConditions() { IfNoneMatch = ETag.All },
                            cancellationToken: c);
                    }
                    catch
                    {
                        // If the attempt to copy failed, check if the blob already exists (created by 
                        // someone else), in which case keep going.
                        if (!await AzureRetry.OrFalse(async () => (await final.ExistsAsync(cancel)).Value))
                            throw;
                    }
                },
                cancel).ConfigureAwait(false);

            // Wait for copy to finish.
            var delay = 250;
            while (true)
            {
                var props = await AzureRetry.Do(
                            c => final.GetPropertiesAsync(cancellationToken: c),
                            cancel).ConfigureAwait(false);

                switch (props.Value.BlobCopyStatus)
                {
                    case CopyStatus.Pending:
                        if (delay <= 120000) delay *= 2;
                        await Task.Delay(TimeSpan.FromMilliseconds(delay), cancel);
                        continue;
                    case CopyStatus.Aborted:
                    case CopyStatus.Failed:
                        throw new Exception("Internal copy for '" + final.Name + "' failed (" + props.Value.BlobCopyStatus + ")");
                    case CopyStatus.Success:
                        return;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        /// <summary>
        /// Get properties of blob to save its creation date and its size in the blob container deleted as a json <see cref="AzureDeletedBlobInfo"/>.
        /// Realm and hash are used the same way as in Persistent to name this new blob.
        /// Blob is then deleted.
        /// </summary>
        /// <param name="hash"> The hash of the blob to be deleted. </param>
        /// <param name="reason"> A string containing the reason for the deletion (human-readable). </param>
        /// <param name="cancel"> Cancellation token. </param>
        public async Task DeleteWithReasonAsync(Hash hash, string reason, CancellationToken cancel)
        {
            var blobToDelete = Persistent.GetBlobClient(AzureBlobName(Realm, hash));
            var blobDeleted = Deleted.GetBlobClient(AzureBlobName(Realm, hash));

            AzureDeletedBlobInfo azureDeletedBlobInfo;
            try
            {
                var props = await blobToDelete.GetPropertiesAsync(cancellationToken: cancel).ConfigureAwait(false);

                azureDeletedBlobInfo = new AzureDeletedBlobInfo
                {
                    Created = props.Value.CreatedOn.DateTime,
                    Deleted = DateTime.Now,
                    Reason = reason,
                    Size = props.Value.ContentLength,
                };
            }
            catch (RequestFailedException e) when (e.Status == 404)
            {
                throw await AzureBlobRef.ReadDeletedBlobAsync(Deleted, Realm, hash, cancel).ConfigureAwait(false);
            }

            string json = JsonConvert.SerializeObject(azureDeletedBlobInfo);
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                await blobDeleted.UploadAsync(ms, new BlobUploadOptions { AccessTier = AccessTier.Cool }, cancel);
            }
            await blobToDelete.DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancel).ConfigureAwait(false);
        }            
    }
}
