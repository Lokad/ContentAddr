namespace Lokad.ContentAddr;

/// <summary> A written blob. </summary>
public struct WrittenBlob
{
    public WrittenBlob(Hash hash, long size)
    {
        Hash = hash;
        Size = size;
    }

    /// <summary> The hash of the written blob. </summary>
    public Hash Hash { get; }

    /// <summary> The size of the written blob, in bytes. </summary>
    public long Size { get; }
}

