using Azure.Storage.Blobs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> Persistent content-addressable store backed by Azure blobs. </summary>
    /// <see cref="AzureReadOnlyStore"/>
    /// <remarks>
    ///     Supports uploading blobs, as well as committing blobs uploaded to a staging
    ///     container.
    /// </remarks>
    public sealed class DualAzureStore : DualAzureReadOnlyStore, IAzureStore
    {
        /// <summary> An AzureStore used for writing functionnalities </summary>
        private AzureStore _newStore;

        /// <param name="realm"> <see cref="AzureReadOnlyStore"/> </param>
        /// <param name="oldPersistent"> 
        ///     Blobs are stored here on the old version, named according to 
        ///     <see cref="AzureReadOnlyStore.AzureBlobName"/>.
        /// </param>
        /// <param name="newPersistent"> 
        ///     Blobs are stored here on the new version, named according to 
        ///     <see cref="AzureReadOnlyStore.AzureBlobName"/>.
        /// </param>
        /// <param name="staging"> Temporary blobs are stored here. </param>
        /// <param name="onCommit"> Called when a blob is committed. </param>
        public DualAzureStore(
            string realm,
            BlobContainerClient oldPersistent,
            BlobContainerClient newPersistent,
            BlobContainerClient staging,
            BlobContainerClient archive,
            BlobContainerClient deleted,
            AzureWriter.OnCommit onCommit = null) : base(realm, oldPersistent, newPersistent, deleted)
        {
            _newStore = new AzureStore(realm, newPersistent, staging, archive, deleted, onCommit);
        }

        /// <see cref="IStore{TBlobRef}.StartWriting"/>
        public StoreWriter StartWriting() =>
            _newStore.StartWriting();

        /// <summary> Get the URL of a temporary blob where data can be uploaded. </summary>
        /// <remarks> 
        ///     Commit blob with <see cref="CommitTemporaryBlob"/>.
        /// 
        ///     The name should be a valid Azure Blob name, but its contents are not important
        ///     (although it is recommended that a "date/realm/guid" format is used to make
        ///     cleanup easier, to prevent cross-realm contamination, and to avoid collisions).
        /// </remarks>
        public Uri GetSignedUploadUrl(string name, TimeSpan life) =>
            _newStore.GetSignedUploadUrl(name, life);

        /// <summary> Commit a blob from staging to the persistent store. </summary>
        /// <remarks>
        ///     Computes the hash of the blob before committing it.
        /// </remarks>
        /// <param name="name"> The full name of the temporary blob. </param>
        /// <param name="cancel"> Cancellation token. </param>
        public Task<IAzureReadBlobRef> CommitTemporaryBlob(string name, CancellationToken cancel) =>
            _newStore.CommitTemporaryBlob(name, cancel);

        /// <summary>
        /// Not supported as it is usually generated as read-only.
        /// </summary>
        public Task DeleteWithReasonAsync(Hash hash, string reason, CancellationToken cancel) => 
            throw new NotSupportedException("Blob deletion is not supported for DualAzureStore");
    }
}
