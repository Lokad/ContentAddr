using Amazon.S3;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> Generates <see cref="S3Store"/> instances for specific accounts. </summary>
    public interface IS3StoreFactory : IStoreFactory
    {
        /// <summary> Called when committing blobs. </summary>
        S3Writer.OnCommit OnCommit { get; set; }

        /// <summary> A read-write store for the specified account. </summary>
        IS3Store ForAccount(long account);

        /// <summary> A read-only store for the specified account. </summary>
        IS3ReadOnlyStore ReadOnlyForAccount(long account);

        /// <summary> The underlying S3 client. </summary>
        IAmazonS3 Client { get; }

        /// <summary> Deletes all contents. Only available when testing. </summary>
        void Delete();

        /// <summary> Retrieve all accounts that have blobs in stores from this factory. </summary>
        /// <remarks> Accounts are sorted in ascending order. </remarks>
        Task<IReadOnlyList<long>> GetAccountsAsync(CancellationToken cancel);
    }
}
