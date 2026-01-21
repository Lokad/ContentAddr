using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace Lokad.ContentAddr.Mapped;

public sealed class MemoryMappedStore : IStore<MemoryMappedBlobRef>, IDisposable
{
    public long Realm { get; }

    private readonly MemoryMappedFile _mmf;

    private readonly MemoryMappedViewAccessor _mmva;

    private readonly bool _leaveOpen;

    /// <summary> For every hash, the offset and length. </summary>
    private readonly Dictionary<Hash, (long Offset, long Count)> _blobs = 
        new();

    /// <summary>
    ///     The number of bytes in this store. Thread-safe. 
    /// </summary>
    /// <remarks> The next atom will be written to this offset. </remarks>
    public long Size { get; private set; }

    /// <summary> Used to ensure accesses are single-threaded. </summary>
    private readonly object _syncroot = new object();

    public MemoryMappedStore(
        long realm, 
        MemoryMappedFile file, 
        bool leaveOpen)
    {
        Realm = realm;
        _mmf = file;
        _mmva = _mmf.CreateViewAccessor();
        _leaveOpen = leaveOpen;
    }

    public MemoryMappedBlobRef this[Hash hash]
    {
        get
        {
            lock (_syncroot)
            {
                // Happy case: the blob is already indexed.
                if (_blobs.TryGetValue(hash, out var blob))
                    return new MemoryMappedBlobRef(Realm, hash, _mmva, blob.Offset, blob.Count);

                // Acceptable case: someone else has written values to the memory map,
                // see if one of them matches the hash.
                while (true)
                {
                    _mmva.Read(Size, out BlobHeader header);
                    if (header.Offset != Size + BlobHeader.Size)
                        break;

                    _blobs[header.Hash] = (header.Offset, header.Count);

                    var size = header.Offset + header.Count;
                    while (size % 8 != 0) ++size;
                    Size = size;

                    if (header.Hash.Equals(hash))
                        return new MemoryMappedBlobRef(Realm, hash, _mmva, header.Offset, header.Count);
                }

                // Blob not found.
                return new MemoryMappedBlobRef(Realm, hash, null, 0, 0);
            }
        }
    }

    /// <summary>
    ///     Drop all atoms beyond the specified offset, reducing the size used by this
    ///     store.
    /// </summary>
    public void Truncate(long size)
    {
        if (size >= Size) return;

        lock (_syncroot)
        {
            // Identify the end of the surviving blob with the highest address,
            // and the list of blobs that must be dropped.
            var max = 0L;
            var toRemove = new List<Hash>();
            foreach (var kv in _blobs)
            {
                var end = kv.Value.Offset + kv.Value.Count;
                if (end >= size)
                    toRemove.Add(kv.Key);
                else
                    max = Math.Max(end, max);
            }

            // Remove the blobs from the index.
            foreach (var hash in toRemove)
                _blobs.Remove(hash);

            // Reduce the size to the end of the surviving blob
            while (max % 8 != 0) ++max;
            Size = max;

            // Erase the written blob.
            var emptyHeader = new BlobHeader();
            _mmva.Write(Size, ref emptyHeader);
            _mmva.Flush();
        }
    }

    IReadBlobRef IReadOnlyStore.this[Hash hash] => this[hash];

    public StoreWriter StartWriting() => 
        new MemoryMappedStoreWriter(this);

    internal void Commit(Hash hash, IReadOnlyList<byte[]> contents)
    {
        // Either the hash exists, or we move 'Size' to the end of the data 
        // available in the file.
        if (this[hash].Exists) return;

        lock (_syncroot)
        {

            var count = 0;
            foreach (var m in contents) count += m.Length;

            var offset = Size + BlobHeader.Size;
            var header = new BlobHeader(offset, count, hash);

            // Maybe someone else locked the object since we last checked for existence
            if (!_blobs.TryAdd(hash, (offset, count)))
                return;

            foreach (var m in contents)
            {
                _mmva.WriteArray(offset, m, 0, m.Length);
                offset += m.Length;
            }

            _mmva.Write(Size, ref header);

            while (offset % 8 != 0) ++offset;
            Size = offset;
        }

        _mmva.Flush();
    }

    public bool IsSameStore(IReadOnlyStore other) =>
        ReferenceEquals(this, other);

    public void Dispose()
    {
        _mmva.Dispose();
        if (!_leaveOpen) _mmf.Dispose();
    }

    /// <summary> Prepended before the data of each blob. </summary>
    [StructLayout(LayoutKind.Explicit, Size = Size)]
    struct BlobHeader
    {
        /// <summary> The offset of the first byte of blob data inside the file. </summary>
        [FieldOffset(0)]
        public readonly long Offset;

        /// <summary> The number of bytes in this blob. </summary>
        [FieldOffset(8)]
        public readonly long Count;

        /// <summary> The hash of this blob. </summary>
        [FieldOffset(16)]
        public readonly Hash Hash;

        /// <summary> Size of the header, in bytes. </summary>
        public const int Size = Hash.Size + 2 * sizeof(long);

        public BlobHeader(long offset, long count, Hash hash)
        {
            Offset = offset;
            Count = count;
            Hash = hash;
        }
    }
}
