using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Mapped
{
    internal class MemoryMappedStoreWriter : StoreWriter
    {
        private readonly MemoryMappedStore _store;

        /// <summary>
        ///     If a blob is very large, we keep its contents in buffers until
        ///     we have received all of it.
        /// </summary>
        private List<byte[]> _accumulatedBuffers = 
            new List<byte[]>();

        public MemoryMappedStoreWriter(MemoryMappedStore memoryMappedStore)
        {
            _store = memoryMappedStore;
        }

        protected override async Task DoOptCommitAsync(Hash hash, Func<Task> optionalWrite, CancellationToken cancel)
        {
            if (_store[hash].Exists) return;

            if (optionalWrite != null) await optionalWrite().ConfigureAwait(false);
            await DoCommitAsync(hash, cancel).ConfigureAwait(false);
        }

        protected override Task DoCommitAsync(Hash hash, CancellationToken cancel) =>
            Task.Run(() => _store.Commit(hash, _accumulatedBuffers));

        protected override Task DoWriteAsync(
            ReadOnlyMemory<byte> buffer, 
            CancellationToken cancel)
        {
            var memory = new byte[buffer.Length];
            _accumulatedBuffers.Add(memory);
            return Task.Run(() => buffer.CopyTo(memory));
        }
    }
}