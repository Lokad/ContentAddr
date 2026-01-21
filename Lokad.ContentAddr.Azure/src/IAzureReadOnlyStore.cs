using System;
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
    public interface IAzureReadOnlyStore : IReadOnlyStore<IAzureReadBlobRef>
    {

        /// <summary>
        ///     Enumerate all blobs with the provided prefix, in ascending hash order,
        ///     invoking the callback with the hash, size and creation date of each blob.
        /// </summary>
        /// <returns> The number of blobs.</returns>
        Task<int> ListBlobsAsync(
            byte prefix,
            Action<Hash, long, DateTime> callback,
            CancellationToken cancel);
    }
}
