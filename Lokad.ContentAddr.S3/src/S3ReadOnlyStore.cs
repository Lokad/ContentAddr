using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> A read-only content-addressable store backed by S3-compatible storage. </summary>
    /// <remarks>
    ///     To avoid cross-account contamination, blobs are stored in *realms*, which are
    ///     prefixes: a blob with hash <c>H</c> in realm <c>R</c> is stored in the bucket
    ///     under the object key <c>persist/R/H</c>.
    /// 
    ///     Each store is a window to a specific realm.
    /// </remarks>
    public class S3ReadOnlyStore : IS3ReadOnlyStore
    {
        protected IAmazonS3 Client { get; }
        protected string Bucket { get; }
        protected string PersistPrefix { get; }
        protected string DeletedPrefix { get; }
        protected string Realm { get; }

        long IReadOnlyStore.Realm => long.Parse(Realm);

        public S3ReadOnlyStore(
            string realm,
            IAmazonS3 client,
            string bucket,
            string persistPrefix,
            string deletedPrefix)
        {
            Realm = realm;
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
            PersistPrefix = NormalizePrefix(persistPrefix);
            DeletedPrefix = NormalizePrefix(deletedPrefix);
        }

        public IS3ReadBlobRef this[Hash hash] =>
            new S3BlobRef(Realm, hash, Client, Bucket, PersistentObjectKey(PersistPrefix, Realm, hash), DeletedPrefix);

        IReadBlobRef IReadOnlyStore.this[Hash hash] => this[hash];

        public static string PersistentObjectKey(string persistPrefix, string realm, Hash hash) =>
            NormalizePrefix(persistPrefix) + realm + "/" + hash;

        public static string PersistentObjectKey(string persistPrefix, long accountId, Hash hash) =>
            PersistentObjectKey(persistPrefix, accountId.ToString(), hash);

        public static string DeletedObjectKey(string deletedPrefix, string realm, Hash hash) =>
            NormalizePrefix(deletedPrefix) + realm + "/" + hash;

        public static string DeletedObjectKey(string deletedPrefix, long accountId, Hash hash) =>
            DeletedObjectKey(deletedPrefix, accountId.ToString(), hash);

        public bool IsSameStore(IReadOnlyStore other)
        {
            if (other is S3ReadOnlyStore s3Ros)
                return s3Ros.Bucket == Bucket &&
                       s3Ros.PersistPrefix == PersistPrefix &&
                       Realm == s3Ros.Realm;

            return false;
        }

        public async Task<int> ListBlobsAsync(
            byte prefix,
            Action<Hash, long, DateTime> callback,
            CancellationToken cancel)
        {
            var blobPrefix = $"{PersistPrefix}{Realm}/{prefix:X2}";
            var count = 0;
            string continuationToken = null;

            do
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = Bucket,
                    Prefix = blobPrefix,
                    ContinuationToken = continuationToken
                };

                var response = await Client.ListObjectsV2Async(request, cancel).ConfigureAwait(false);
                foreach (var obj in response.S3Objects)
                {
                    var bname = obj.Key;
                    if (!Hash.TryParse(bname.Substring(bname.Length - 32), out var hash)) continue;

                    ++count;
                    callback(hash, obj.Size, obj.LastModified.ToUniversalTime());
                }

                continuationToken = response.IsTruncated ? response.NextContinuationToken : null;
            }
            while (continuationToken != null);

            return count;
        }

        public async Task<bool> ListIfFewBlobsAsync(
            Action<Hash, long, DateTime> callback,
            CancellationToken cancel)
        {
            var blobPrefix = $"{PersistPrefix}{Realm}/";

            var request = new ListObjectsV2Request
            {
                BucketName = Bucket,
                Prefix = blobPrefix,
                MaxKeys = 1000
            };

            var response = await Client.ListObjectsV2Async(request, cancel).ConfigureAwait(false);
            if (response.IsTruncated) return false;

            foreach (var obj in response.S3Objects)
            {
                var bname = obj.Key;
                if (!Hash.TryParse(bname.Substring(bname.Length - 32), out var hash)) continue;

                callback(hash, obj.Size, obj.LastModified.ToUniversalTime());
            }

            return true;
        }

        protected static string NormalizePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
            return prefix.EndsWith("/") ? prefix : prefix + "/";
        }
    }
}
