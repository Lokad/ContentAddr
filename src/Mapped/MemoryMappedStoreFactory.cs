using System;
using System.IO.MemoryMappedFiles;

namespace Lokad.ContentAddr.Mapped
{
    /// <summary>
    ///     A single-realm store factory, only creates (and allows) 
    ///     stores for realm 0.
    /// </summary>
    public sealed class MemoryMappedStoreFactory : IStoreFactory, IDisposable
    {
        public MemoryMappedStore SingleStore { get; }

        public MemoryMappedStoreFactory(MemoryMappedFile mmf, bool leaveOpen = false)
        {
            SingleStore = new MemoryMappedStore(0, mmf, leaveOpen);
        }

        /// <see cref="IStoreFactory.this"/>
        public IStore<IReadBlobRef> this[long account]  =>
            account == SingleStore.Realm ? SingleStore : 
            throw new ArgumentException($"Expected account = {SingleStore.Realm}");

        /// <see cref="IStoreFactory.ReadOnlyStore"/>
        public IReadOnlyStore<IReadBlobRef> ReadOnlyStore(long account) => this[account];

        /// <see cref="IStoreFactory.Describe"/>
        public string Describe() => "[CAS] memory-mapped file";

        public void Dispose()
        {
            SingleStore.Dispose();
        }
    }
}
