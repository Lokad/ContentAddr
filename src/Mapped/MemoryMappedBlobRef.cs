using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Mapped
{
    /// <summary> A reference to a blob in a memory-mapped persistent store. </summary>
    public sealed class MemoryMappedBlobRef : IReadBlobRef
    {
        /// <summary> The realm of this blob. </summary>
        public long Realm { get; }

        /// <summary> The hash of this blob. </summary>
        public Hash Hash { get; }

        /// <summary> The backing memory, null if the blob does not exist. </summary>
        private readonly MemoryMappedViewAccessor _mmva = null;

        /// <summary> The start offset inside the `_buffer`. </summary>
        private readonly long _offset;

        /// <summary> The number of bytes. </summary>
        private readonly long _count;

        public MemoryMappedBlobRef(
            long realm, 
            Hash hash, 
            MemoryMappedViewAccessor buffer,
            long offset,
            long count)
        {
            Realm = realm;
            Hash = hash;
            _mmva = buffer;
            _offset = offset;
            _count = count;
        }

        /// <summary> True if this blob exists in the store. </summary>
        public bool Exists => _mmva != null;

        public Task<bool> ExistsAsync(CancellationToken cancel) =>
            Task.FromResult(Exists);

        public Task<long> GetSizeAsync(CancellationToken cancel)
        {
            if (_mmva != null)
                return Task.FromResult(_count);

            throw new NoSuchBlobException(Realm.ToString(), Hash);
        }

        public Task<Stream> OpenAsync(CancellationToken cancel)
        {
            if (_mmva != null)
                return Task.FromResult<Stream>(new ReadMemoryStream(_mmva, _offset, _count));

            throw new NoSuchBlobException(Realm.ToString(), Hash);
        }
    }
}
