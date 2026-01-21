using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> A read-only content-addressable store backed by Azure Storage blobs. </summary>
    /// <remarks>
    ///     To avoid cross-account contamination, blobs are stored in *realms*, which are
    ///     prefixes: a blob with hash <c>H</c> in realm <c>R</c> is stored in the container 
    ///     under the blob name <c>R/H</c>.
    /// 
    ///     Each store is a window to a specific realm. 
    ///     Azure Storage Blobs may be stored on two different version of the storage. 
    ///     This store can deal with blobs from both versions, 
    ///     considering on as the new version and the other as the old version. 
    ///     All new blobs will be written on the new version, reading is performed on both, using the new version if possible. 
    ///     Blobs only existing on the old version will be copied to the new one when consulted.
    ///     
    /// </remarks>
    public class DualAzureReadOnlyStore : IAzureReadOnlyStore
    {
        /// <summary> The container where blobs are persisted on the old version. </summary>
        protected BlobContainerClient OldPersistent { get; }

        /// <summary> The container where blobs are persisted on the new version. </summary>
        protected BlobContainerClient NewPersistent { get; }

        /// <summary> The container saving the blobs deletions. </summary>
        protected BlobContainerClient Deleted { get; }

        /// <summary> The realm for this store. </summary>
        protected string Realm { get; }

        /// <see cref="IReadOnlyStore.Realm"/>
        long IReadOnlyStore.Realm => long.Parse(Realm);

        public DualAzureReadOnlyStore(string realm, BlobContainerClient oldPersistent, BlobContainerClient newPersistent, BlobContainerClient deleted)
        {
            OldPersistent = oldPersistent;
            NewPersistent = newPersistent;
            Realm = realm;
            Deleted = deleted;
        }

        /// <see cref="IReadOnlyStore{TBlobRef}"/>
        public IAzureReadBlobRef this[Hash hash] =>
            new DualAzureBlobRef(
                Realm,
                hash,
                OldPersistent.GetBlobClient(AzureBlobName(Realm, hash)),
                NewPersistent.GetBlobClient(AzureBlobName(Realm, hash)),
                Deleted);

        /// <summary>
        ///     Enumerate all blobs with the provided prefix, in ascending hash order per container,
        ///     invoking the callback with the hash, size and creation date of each blob.
        /// </summary>
        /// <remarks>As stated, the list will be built through the two stores, so
        /// globally the blobs will _not_ be listed in ascending order.</remarks>
        /// <returns> The number of blobs.</returns>
        public async Task<int> ListBlobsAsync(
            byte prefix,
            Action<Hash, long, DateTime> callback,
            CancellationToken cancel)
        {
            var blobPrefix = $"{Realm}/{prefix:X2}";
            var count = 0;

            foreach (var persistent in new[] { OldPersistent, NewPersistent })
            {
                var list = persistent.GetBlobsAsync(traits: BlobTraits.Metadata,
                    states: BlobStates.None,
                    prefix: blobPrefix,
                    cancellationToken: cancel);

                await foreach (var bi in list)
                {
                    var bname = bi.Name;
                    if (!Hash.TryParse(bname.Substring(bname.Length - 32), out var hash)) continue;
                    if (!(bi.Properties.LastModified is DateTimeOffset dto)) continue;

                    ++count;
                    callback(hash, bi.Properties.ContentLength.Value, dto.UtcDateTime);
                }
            }

            return count;
        }

        IReadBlobRef IReadOnlyStore.this[Hash hash] => this[hash];

        /// <summary> Format the name of a block from its realm and content hash. </summary>
        public static string AzureBlobName(string realm, Hash hash) =>
            AzureReadOnlyStore.AzureBlobName(realm, hash);

        /// <summary> Format the name of a block from its account id and content hash. </summary>
        public static string AzureBlobName(long accountId, Hash hash) =>
            AzureReadOnlyStore.AzureBlobName(accountId, hash);

        public bool IsSameStore(IReadOnlyStore other)
        {
            if (other is DualAzureReadOnlyStore aros)
                return aros.NewPersistent.Uri.Equals(NewPersistent.Uri) &&
                       aros.OldPersistent.Uri.Equals(OldPersistent.Uri) &&
                       Realm == aros.Realm;

            return false;
        }

    }
}

