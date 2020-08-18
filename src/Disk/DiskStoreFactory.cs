namespace Lokad.ContentAddr.Disk
{
    public sealed class DiskStoreFactory : IStoreFactory
    {
        /// <summary> The root directory of the store. </summary>
        private readonly string _path;

        public DiskStoreFactory(string path) { _path = path; }

        /// <see cref="IStoreFactory.this"/>
        IStore<IReadBlobRef> IStoreFactory.this[long account] => this[account];

        /// <see cref="IStoreFactory.this"/>
        public DiskStore this[long account] => 
            new DiskStore(DiskStorePaths.PathOfAccount(_path, account));

        /// <see cref="IStoreFactory.ReadOnlyStore"/>
        public IReadOnlyStore<IReadBlobRef> ReadOnlyStore(long account) => this[account];

        /// <see cref="IStoreFactory.Describe"/>
        public string Describe() => "[CAS] " + _path;
    }
}
