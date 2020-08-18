using System.Collections.Concurrent;

namespace Lokad.ContentAddr.Memory
{
    public sealed class MemoryStoreFactory : IStoreFactory
    {
        /// <summary> All in-memory stores, by account. </summary>
        private readonly ConcurrentDictionary<long, MemoryStore> _stores = 
            new ConcurrentDictionary<long, MemoryStore>();

        /// <see cref="IStoreFactory.this"/>
        public IStore<IReadBlobRef> this[long account] =>
            _stores.GetOrAdd(account, a => new MemoryStore());
        
        /// <see cref="IStoreFactory.ReadOnlyStore"/>
        public IReadOnlyStore<IReadBlobRef> ReadOnlyStore(long account) => this[account];

        /// <see cref="IStoreFactory.Describe"/>
        public string Describe() => "[CAS] memory " + _stores.GetHashCode().ToString("X8");
    }
}
