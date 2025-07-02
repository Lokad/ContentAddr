using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Memory;

/// <summary> Read blob reference. </summary>
public class InMemoryBlobRef : IReadBlobRef
{
    /// <summary> Reference to the original file dictionary. </summary>
    private readonly IDictionary<Hash, byte[]> _files;

    public InMemoryBlobRef(IDictionary<Hash, byte[]> files, Hash hash)
    {
        _files = files;
        Hash = hash;
    }

    /// <inheritdoc/>
    public Hash Hash { get; }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(CancellationToken cancel)
    {
        var exists = false;
        lock (_files)
        {
            exists = _files.ContainsKey(Hash);
        }

        return Task.FromResult(exists);
    }

    /// <inheritdoc/>
    public Task<long> GetSizeAsync(CancellationToken cancel)
    {
        byte[] file;
        var exists = false;
        lock (_files)
        {
            exists = _files.TryGetValue(Hash, out file);
        }

        if (!exists)
            // We fake the realm, since in-memory stores do not support it.
            throw new NoSuchBlobException("in-memory", Hash);

        return Task.FromResult((long)file.Length);
    }

    /// <inheritdoc/>
    public Task<Stream> OpenAsync(CancellationToken cancel)
    {
        byte[] file;
        var exists = false;
        lock (_files)
        {
            exists = _files.TryGetValue(Hash, out file);
        }

        if (!exists)
            // We fake the realm, since in-memory stores do not support it.
            throw new NoSuchBlobException("in-memory", Hash);

        return Task.FromResult<Stream>(new MemoryStream(file, writable: false));
    }
}