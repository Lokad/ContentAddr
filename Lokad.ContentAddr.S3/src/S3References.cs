using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> Persistence of blob references, as a sidecar object next to the blob. </summary>
    /// <remarks>
    ///     The references of the blob at key <c>K</c> are stored in object <c>K.refs</c>, as
    ///     the raw bytes of the distinct hashes, concatenated. The object is absent if the 
    ///     blob has no references.
    /// </remarks>
    internal static class S3References
    {
        public static string KeyOf(string blobKey) => blobKey + ".refs";

        /// <summary> Reads the references, returns an empty list if the sidecar does not exist. </summary>
        public static async Task<IReadOnlyList<Hash>> ReadAsync(
            IAmazonS3 client, string bucket, string blobKey, CancellationToken cancel)
        {
            byte[] bytes;
            try
            {
                using (var response = await client.GetObjectAsync(bucket, KeyOf(blobKey), cancel).ConfigureAwait(false))
                using (var ms = new MemoryStream())
                {
                    await response.ResponseStream.CopyToAsync(ms, 81920, cancel).ConfigureAwait(false);
                    bytes = ms.ToArray();
                }
            }
            catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound || e.ErrorCode == "NoSuchKey")
            {
                return Array.Empty<Hash>();
            }

            var refs = new Hash[bytes.Length / Hash.Size];
            for (var i = 0; i < refs.Length; ++i)
                refs[i] = Hash.FromBytes(bytes, i * Hash.Size);
            return refs;
        }

        /// <summary> Writes the references, unless the sidecar already exists (existing references win). </summary>
        public static async Task WriteIfAbsentAsync(
            IAmazonS3 client, string bucket, string blobKey, IReadOnlyList<Hash> references, CancellationToken cancel)
        {
            if (references.Count == 0) return;

            var key = KeyOf(blobKey);
            if (await S3Store.ObjectExistsAsync(client, bucket, key, cancel).ConfigureAwait(false)) return;

            var bytes = new byte[references.Count * Hash.Size];
            for (var i = 0; i < references.Count; ++i)
                references[i].ToBytes(bytes, i * Hash.Size);

            using (var ms = new MemoryStream(bytes))
            {
                await client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    InputStream = ms
                }, cancel).ConfigureAwait(false);
            }
        }
    }
}
