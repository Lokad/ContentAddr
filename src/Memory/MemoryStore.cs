using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Memory
{
    /// <summary> A content-addressable store that keeps all data in memory. </summary>
    /// <remarks> Intended for testing. </remarks>
    public sealed class MemoryStore : IStore<InMemoryBlobRef>
    {
        /// <summary> All files, as byte buffers. </summary>
        private readonly IDictionary<Hash, byte[]> _files = new Dictionary<Hash, byte[]>();

        /// <see cref="IReadOnlyStore{TBlobRef}"/>
        IReadBlobRef IReadOnlyStore.this[Hash hash] => this[hash];

        public InMemoryBlobRef this[Hash hash] => new InMemoryBlobRef(_files, hash);

        /// <see cref="IStore{TBlobRef}"/>
        public StoreWriter StartWriting() => new Writer(_files);

        public long Realm => 0;

        /// <summary>
        /// Remove the blob from the store.
        /// </summary>
        /// <remarks> Used for unit testing purposes. </remarks>
        public bool RemoveFromStore(Hash hash)
        {
            lock (_files)
            {
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
            private readonly MemoryStream _stream = new MemoryStream();

            public Writer(IDictionary<Hash, byte[]> files)
            {
                _files = files;
            }

            /// <see cref="StoreWriter.DoWriteAsync"/>
            protected override Task DoWriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                _stream.WriteAsync(buffer, offset, count, cancel);

            /// <see cref="StoreWriter.DoCommitAsync"/>
            protected override Task DoCommitAsync(Hash hash, CancellationToken cancel)
            {
                lock (_files)
                {
                    _files[hash] = _stream.ToArray();
                }

                return Task.FromResult(true);
            }
        }
    }
}
