using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> Persistence of blob references, as a sidecar blob next to the blob. </summary>
    /// <remarks>
    ///     The references of the blob named <c>N</c> are stored in blob <c>N.refs</c>, as
    ///     the raw bytes of the distinct hashes, concatenated. The blob is absent if the 
    ///     blob has no references.
    /// </remarks>
    internal static class AzureReferences
    {
        public static string NameOf(string blobName) => blobName + ".refs";

        /// <summary> Reads the references, returns an empty list if the sidecar does not exist. </summary>
        public static async Task<IReadOnlyList<Hash>> ReadAsync(BlobClient sidecar, CancellationToken cancel)
        {
            byte[] bytes;
            try
            {
                var download = await sidecar.DownloadContentAsync(cancel).ConfigureAwait(false);
                bytes = download.Value.Content.ToArray();
            }
            catch (RequestFailedException e) when (e.Status == 404)
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
            BlobClient sidecar, IReadOnlyList<Hash> references, CancellationToken cancel)
        {
            if (references.Count == 0) return;

            var bytes = new byte[references.Count * Hash.Size];
            for (var i = 0; i < references.Count; ++i)
                references[i].ToBytes(bytes, i * Hash.Size);

            try
            {
                using (var ms = new MemoryStream(bytes))
                {
                    await sidecar.UploadAsync(ms, new BlobUploadOptions
                    {
                        Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }
                    }, cancel).ConfigureAwait(false);
                }
            }
            catch (RequestFailedException e) when (e.Status == 409 || e.Status == 412)
            {
                // The sidecar already exists, its references win.
            }
        }
    }
}
