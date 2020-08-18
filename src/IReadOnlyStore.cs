using System.Threading.Tasks;

namespace Lokad.ContentAddr
{
    public delegate Task<IReadOnlyStore> AsyncStoreGetter(long account);

    public interface IReadOnlyStore
    {
        IReadBlobRef this[Hash hash] { get; }

        /// <summary> True if a store is the exact same as another. </summary>
        bool IsSameStore(IReadOnlyStore other);

        long Realm { get; }
    }

    /// <summary> A read-only content-addressable store. </summary>    
    public interface IReadOnlyStore<out TBlobRef> : IReadOnlyStore where TBlobRef : IReadBlobRef
    {
        /// <summary> Reference a blob by its hash. </summary>        
        new TBlobRef this[Hash hash] { get; }
    }
}
