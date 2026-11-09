using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Disk;

/// <summary> References a blob stored in an on-disk file. </summary>
public sealed class DiskBlobRef : IReadBlobRef
{
    /// <summary> The path of the file. </summary>
    private readonly string _path;

    public DiskBlobRef(Hash hash, string path)
    {
        _path = path;
        Hash = hash;
    }

    /// <inheritdoc/>
    public Hash Hash { get; }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(CancellationToken cancel) =>
        Task.FromResult(File.Exists(_path));

    /// <inheritdoc/>
    public Task<long> GetSizeAsync(CancellationToken cancel)
    {
        var info = new FileInfo(_path);
        if (!info.Exists)
            throw new NoSuchBlobException(Path.GetDirectoryName(Path.GetDirectoryName(_path)), Hash);

        return Task.FromResult(info.Length);
    }

    /// <inheritdoc/>
    public Task<Stream> OpenAsync(CancellationToken cancel)
    {
        try
        {
            var stream = File.Open(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult<Stream>(stream);
        }
        catch (FileNotFoundException)
        {
            throw new NoSuchBlobException(Path.GetDirectoryName(Path.GetDirectoryName(_path)), Hash);
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Hash>> GetReferencesAsync(CancellationToken cancel)
    {
        if (!File.Exists(_path))
            throw new NoSuchBlobException(Path.GetDirectoryName(Path.GetDirectoryName(_path)), Hash);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(DiskStorePaths.RefsPathOfBlobPath(_path));
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<IReadOnlyList<Hash>>(Array.Empty<Hash>());
        }

        var refs = new Hash[bytes.Length / Hash.Size];
        for (var i = 0; i < refs.Length; ++i)
            refs[i] = Hash.FromBytes(bytes.AsSpan(i * Hash.Size, Hash.Size));

        return Task.FromResult<IReadOnlyList<Hash>>(refs);
    }
}