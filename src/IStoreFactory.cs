namespace Lokad.ContentAddr
{
    /// <summary> Generates stores for individual accounts. </summary>
    public interface IStoreFactory
    {
        /// <summary> Retrieve a store for the specified account. </summary>
        IStore<IReadBlobRef> this[long account] { get; }

        /// <summary> Retrieve a read only store for the specified account. </summary>
        IReadOnlyStore<IReadBlobRef> ReadOnlyStore(long account);

        /// <summary> Describe this factory. </summary>
        /// <remarks> 
        ///     Used to check that two servers are using the same store, 
        ///     without transmitting connection strings over the network.
        /// </remarks>
        string Describe();
    }
}
