using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Mapped;

internal class MemoryMappedStoreWriter : StoreWriter
{
    private readonly MemoryMappedStore _store;

    /// <summary>
    ///     If a blob is very large, we keep its contents in buffers until
    ///     we have received all of it.
    /// </summary>
    private readonly List<byte[]> _accumulatedBuffers = new();

    public MemoryMappedStoreWriter(MemoryMappedStore memoryMappedStore)
    {
        _store = memoryMappedStore;
    }

    protected override async Task DoOptCommitAsync(Hash hash, IReadOnlyList<Hash> references, Func<Task> optionalWrite, CancellationToken cancel)
    {
        if (_store[hash].Exists)
        {
            // Nothing to write, except references if the blob has none.
            if (references.Count > 0)
                await DoCommitAsync(hash, references, cancel).ConfigureAwait(false);
            return;
        }

        if (optionalWrite != null) await optionalWrite().ConfigureAwait(false);
        await DoCommitAsync(hash, references, cancel).ConfigureAwait(false);
    }

    protected override Task DoCommitAsync(Hash hash, IReadOnlyList<Hash> references, CancellationToken cancel) =>
        Task.Run(() => _store.Commit(hash, _accumulatedBuffers, references), cancel);

    protected override Task DoWriteAsync(
        ReadOnlyMemory<byte> buffer, 
        CancellationToken cancel)
    {
        var memory = new byte[buffer.Length];
        _accumulatedBuffers.Add(memory);
        return Task.Run(() => buffer.CopyTo(memory), cancel);
    }
}