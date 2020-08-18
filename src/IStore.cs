namespace Lokad.ContentAddr
{
    public interface IStore : IReadOnlyStore, IWriteOnlyStore
    {
    }

    /// <summary> A readable and writable content-addressed store. </summary>
    public interface IStore<out TBlobRef> : IStore, IReadOnlyStore<TBlobRef> where TBlobRef : IReadBlobRef
    {        
    }
}
