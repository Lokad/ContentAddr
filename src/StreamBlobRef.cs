using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr
{
    /// <summary>
    ///     An implementation of <see cref="IReadBlobRef"/> based on 
    ///     manually provided size, hash and stream-opener function.     
    /// </summary>
    /// <remarks>
    ///     This is a semi-hackish way to provide an <see cref="IReadBlobRef"/>
    ///     to code that expects it, when no actual content-addressable store is 
    ///     available (just the data itself).
    /// 
    ///     There is a small risk if the stream-opening function can only be 
    ///     called once, that the code that uses the blob reference attempts to
    ///     open it more than once (but, in practice, this is bad design and 
    ///     should not happen).
    /// </remarks>
    public sealed class StreamBlobRef : IReadBlobRef
    {
        /// <summary> The size of the stream. </summary>
        private readonly long _size;

        /// <summary> The stream opener. </summary>
        private readonly Func<CancellationToken, Task<Stream>> _open;

        public StreamBlobRef(Func<CancellationToken, Task<Stream>> open, long size, Hash hash)
        {
            _open = open;
            _size = size;
            Hash = hash;
        }

        public Hash Hash { get; }

        /// <see cref="IReadBlobRef.ExistsAsync"/>
        public Task<bool> ExistsAsync(CancellationToken cancel) => Task.FromResult(true);

        /// <see cref="IReadBlobRef.GetSizeAsync"/>
        public Task<long> GetSizeAsync(CancellationToken cancel) => Task.FromResult(_size);

        /// <see cref="IReadBlobRef.OpenAsync"/>
        public Task<Stream> OpenAsync(CancellationToken cancel) => _open(cancel);
    }
}
