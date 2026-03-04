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
        event S3Writer.OnCommit OnCommit;

        /// <summary>
        /// Called whenever bytes are uploaded to a staging blob.
        /// </summary>
        /// <remarks>
        /// Parameters are: realm id, staging blob reference (full temporary S3 key), and uploaded byte count.
        /// </remarks>
        event S3PreWriter.OnStagingDataSent OnStagingDataSent;

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

        /// <summary>
        ///     Deletes temporary blobs from the staging prefix that are older than 2 days.
        /// </summary>
        Task RemoveOldStagingAsync(CancellationToken cancel);
    }
}
