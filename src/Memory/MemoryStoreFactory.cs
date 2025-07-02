using System.Collections.Concurrent;

namespace Lokad.ContentAddr.Memory;

public sealed class MemoryStoreFactory : IStoreFactory
{
    /// <summary> All in-memory stores, by account. </summary>
    private readonly ConcurrentDictionary<long, MemoryStore> _stores = 
        new();

    /// <inheritdoc/>
    public IStore<IReadBlobRef> this[long account] =>
        _stores.GetOrAdd(account, _ => new MemoryStore());

    /// <inheritdoc/>
    public IReadOnlyStore<IReadBlobRef> ReadOnlyStore(long account) => this[account];

    /// <inheritdoc/>
    public string Describe() => "[CAS] memory " + _stores.GetHashCode().ToString("X8");
}
