namespace Lokad.ContentAddr.Disk;

public sealed class DiskStoreFactory : IStoreFactory
{
    /// <summary> The root directory of the store. </summary>
    private readonly string _path;

    public DiskStoreFactory(string path) { _path = path; }

    /// <inheritdoc/>
    IStore<IReadBlobRef> IStoreFactory.this[long account] => this[account];

    /// <inheritdoc/>
    public DiskStore this[long account] => 
        new(DiskStorePaths.PathOfAccount(_path, account));

    /// <inheritdoc/>
    public IReadOnlyStore<IReadBlobRef> ReadOnlyStore(long account) => this[account];

    /// <inheritdoc/>
    public string Describe() => "[CAS] " + _path;
}
