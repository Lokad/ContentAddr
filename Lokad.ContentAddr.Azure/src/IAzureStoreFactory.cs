using Azure.Storage.Blobs;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> Generates <see cref="AzureStore"/> instances for specific accounts. </summary>
    public interface IAzureStoreFactory : IStoreFactory
    {

        /// <summary> Called when committing blobs. </summary>
        AzureWriter.OnCommit OnCommit { get; set; }

        /// <summary> A read-write store for the specified account. </summary>
        IAzureStore ForAccount(long account);

        /// <summary> A read-only store for the specified account. </summary>
        IAzureReadOnlyStore ReadOnlyForAccount(long account);

        /// <summary>
        /// The client for the newest version of the storage
        /// </summary>
        BlobServiceClient BlobClient { get; }

        void Delete();

        /// <summary> Retrieve all accounts that have blobs in stores from this factory. </summary>
        /// <remarks> Accounts are sorted in ascending order. </remarks>
        Task<IReadOnlyList<long>> GetAccountsAsync(CancellationToken cancel);
    }
}

