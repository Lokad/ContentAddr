using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Memory;

/// <summary> A content-addressable store that keeps all data in memory. </summary>
/// <remarks> Intended for testing. </remarks>
public sealed class MemoryStore : IStore<InMemoryBlobRef>
{
    /// <summary> All files, as byte buffers. </summary>
    private readonly IDictionary<Hash, byte[]> _files = new Dictionary<Hash, byte[]>();

    /// <summary> References of blobs (only for blobs that have any). </summary>
    private readonly IDictionary<Hash, IReadOnlyList<Hash>> _refs = new Dictionary<Hash, IReadOnlyList<Hash>>();

    /// <inheritdoc/>
    IReadBlobRef IReadOnlyStore.this[Hash hash] => this[hash];

    public InMemoryBlobRef this[Hash hash] => new(_files, hash, _refs);

    /// <inheritdoc/>
    public StoreWriter StartWriting() => new Writer(_files, _refs);

    public long Realm => 0;

    /// <summary>
    /// Remove the blob from the store.
    /// </summary>
    /// <remarks> Used for unit testing purposes. </remarks>
    public bool RemoveFromStore(Hash hash)
    {
        lock (_files)
        {
            _refs.Remove(hash);
            return _files.Remove(hash);
        }
    }

    public bool IsSameStore(IReadOnlyStore other) => ReferenceEquals(other, this);

    /// <summary> Enumerate all blobs in this store. </summary>
    public IEnumerable<KeyValuePair<Hash, byte[]>> Blobs => _files;

    public sealed class Writer : StoreWriter
    {
        /// <summary> Reference to the original file dictionary. </summary>
        private readonly IDictionary<Hash, byte[]> _files;

        /// <summary> Bytes are appended to this stream. </summary>
        private readonly MemoryStream _stream = new();

        private readonly IDictionary<Hash, IReadOnlyList<Hash>> _refs;

        public Writer(IDictionary<Hash, byte[]> files, IDictionary<Hash, IReadOnlyList<Hash>> refs)
        {
            _files = files;
            _refs = refs;
        }

        /// <inheritdoc cref="StoreWriter.DoWriteAsync"/>
        protected override Task DoWriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel) =>
            _stream.WriteAsync(buffer, cancel).AsTask();

        /// <inheritdoc cref="StoreWriter.DoCommitAsync"/>
        protected override Task DoCommitAsync(Hash hash, IReadOnlyList<Hash> references, CancellationToken cancel)
        {
            lock (_files)
            {
                _files[hash] = _stream.ToArray();

                // Existing references win, unless there are none.
                if (references.Count > 0 && !_refs.ContainsKey(hash))
                    _refs[hash] = references.ToArray();
            }

            return Task.FromResult(true);
        }
    }
}
