namespace Lokad.ContentAddr;

/// <summary> A write-only content-addressable store. </summary> 
public interface IWriteOnlyStore
{
    /// <summary> Open a store writer. </summary>
    StoreWriter StartWriting();
}
