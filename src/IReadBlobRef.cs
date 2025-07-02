using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr;

/// <summary> A readable blob reference in a store.</summary>
/// <remarks> The blob does not necessarily exist. </remarks>
public interface IReadBlobRef
{
    /// <summary> The hash of this blob. </summary>
    Hash Hash { get; }

    /// <summary> Determine whether the blob exists. </summary>
    Task<bool> ExistsAsync(CancellationToken cancel);

    /// <summary> Retrieve the size of the blob. </summary>
    /// <exception cref="NoSuchBlobException"> If the blob does not exist. </exception>
    Task<long> GetSizeAsync(CancellationToken cancel);

    /// <summary> Open blob for reading. </summary>
    /// <remarks> Stream is seekable, read-only. </remarks>
    /// <exception cref="NoSuchBlobException"> If the blob does not exist. </exception>
    Task<Stream> OpenAsync(CancellationToken cancel);
}