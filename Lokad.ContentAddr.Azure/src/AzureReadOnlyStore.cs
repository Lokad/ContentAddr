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
    /// </remarks>
    public class AzureReadOnlyStore : IAzureReadOnlyStore
    {
        /// <summary> The container where blobs are persisted. </summary>
        protected BlobContainerClient Persistent { get; }

        /// <summary> The container saving the blobs deletions </summary>
        protected BlobContainerClient Deleted { get; }

        /// <summary> The realm for this store. </summary>
        protected string Realm { get; }

        /// <see cref="IReadOnlyStore.Realm"/>
        long IReadOnlyStore.Realm => long.Parse(Realm);

        public AzureReadOnlyStore(string realm, BlobContainerClient persistent, BlobContainerClient deleted)
        {
            Persistent = persistent;
            Realm = realm;
            Deleted = deleted;
        }

        /// <see cref="IReadOnlyStore{TBlobRef}"/>
        public IAzureReadBlobRef this[Hash hash] =>
            new AzureBlobRef(Realm, hash, Persistent.GetBlobClient(AzureBlobName(Realm, hash)), Deleted);

        IReadBlobRef IReadOnlyStore.this[Hash hash] => this[hash];

        /// <summary> Format the name of a block from its realm and content hash. </summary>
        public static string AzureBlobName(string realm, Hash hash) =>
            realm + "/" + hash;

        /// <summary> Format the name of a block from its account id and content hash. </summary>
        public static string AzureBlobName(long accountId, Hash hash) => AzureBlobName(accountId.ToString(), hash);

        public bool IsSameStore(IReadOnlyStore other)
        {
            if (other is AzureReadOnlyStore aros)
                return aros.Persistent.Uri.Equals(Persistent.Uri) &&
                       Realm == aros.Realm;

            return false;
        }

        /// <see cref="IAzureReadOnlyStore.ListBlobsAsync"/>
        public async Task<int> ListBlobsAsync(
            byte prefix,
            Action<Hash, long, DateTime> callback,
            CancellationToken cancel)
        {
            var blobPrefix = $"{Realm}/{prefix:X2}";
            var count = 0;

            var list = Persistent.GetBlobsAsync(traits: BlobTraits.Metadata,
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

            return count;
        }

        public async Task<bool> ListIfFewBlobsAsync(
            Action<Hash, long, DateTime> callback,
            CancellationToken cancel)
        {
            var blobPrefix = $"{Realm}/";

            var token = default(string);

            var pages = Persistent.GetBlobsAsync(traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: blobPrefix,
                cancellationToken: cancel)
                .AsPages(token);

            var lonePage = await pages.FirstOrDefaultAsync(cancel);
            if (lonePage.ContinuationToken != null) return false;

            foreach (var bi in lonePage.Values)
            {
                var bname = bi.Name;
                if (!Hash.TryParse(bname.Substring(bname.Length - 32), out var hash)) continue;
                if (!(bi.Properties.LastModified is DateTimeOffset dto)) continue;

                callback(hash, bi.Properties.ContentLength.Value, dto.UtcDateTime);
            }

            return true;
        }
    }
}
