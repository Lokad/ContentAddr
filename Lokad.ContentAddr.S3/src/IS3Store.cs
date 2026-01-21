using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> Persistent content-addressable store backed by S3-compatible storage. </summary>
    /// <see cref="S3ReadOnlyStore"/>
    /// <remarks>
    ///     Supports uploading blobs, as well as committing blobs uploaded to a staging
    ///     prefix.
    /// </remarks>
    public interface IS3Store : IS3ReadOnlyStore, IStore<IS3ReadBlobRef>
    {
        /// <summary> Commit a blob from staging to the persistent store. </summary>
        /// <remarks>
        ///     Computes the hash of the blob before committing it.
        /// </remarks>
        /// <param name="name"> The full object key of the temporary blob. </param>
        /// <param name="cancel"> Cancellation token. </param>
        Task<IS3ReadBlobRef> CommitTemporaryBlob(string name, CancellationToken cancel);

        /// <summary>
        /// Get properties of blob to save its creation date and its size in the deleted prefix as JSON
        /// <see cref="S3DeletedBlobInfo"/>. Realm and hash are used the same way as in Persistent to
        /// name this new blob. Blob is then deleted.
        /// </summary>
        /// <param name="hash"> The hash of the blob to be deleted. </param>
        /// <param name="reason"> A string containing the reason for the deletion (human-readable). </param>
        /// <param name="cancel"> Cancellation token. </param>
        Task DeleteWithReasonAsync(Hash hash, string reason, CancellationToken cancel);
    }
}
