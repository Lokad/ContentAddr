using Azure.Storage.Blobs;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs.Models;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> The <see cref="ContentAddr.IAzureReadBlobRef"/> for an azure dual persistent store. </summary>
    /// <see cref="DualAzureReadOnlyStore"/>
    /// <remarks>
    ///     Exposes the Azure Storage blob itself either in <see cref="OldBlob"/> (a blob on the old storage version) or 
    ///     in <see cref="NewBlob"/> (a blob on the new storage version). 
    ///     Reading is performed on NewBlob if it exists, 
    ///     otherwise it is performed on OldBlob and a copy from Oldblob to NewBlob is automatically requested.
    ///     
    /// </remarks>
    public sealed class DualAzureBlobRef : IAzureReadBlobRef
    {
        public DualAzureBlobRef(
            string realm,
            Hash hash,
            BlobClient oldBlob,
            BlobClient newBlob,
            BlobContainerClient deleted)
        {
            Hash = hash;
            Realm = realm;
            OldBlob = oldBlob;
            NewBlob = newBlob;
            Deleted = deleted;
        }

        /// <see cref="IReadBlobRef.Hash"/>
        public Hash Hash { get; }

        /// <summary> The blob name prefix. </summary>
        /// <remarks>
        ///     Used to avoid accidentally mixing blobs from separate customers
        ///     by using a separate prefix (the "realm") for each customer.
        /// </remarks>
        public string Realm { get; }

        /// <summary> The Azure Storage blob where the blob data might be stored if it exists on the old version of the storage. </summary>
        public BlobClient OldBlob { get; }

        /// <summary> The Azure Storage blob where the blob data might be stored if it exists on the new version of the storage. </summary>
        public BlobClient NewBlob { get; }

        /// <summary> The actual Storage blob where reading is performed : NewBlob if it exists, Oldblob otherwise
        /// </summary>
        private AzureBlobRef _chosen;

        /// <summary> The Azure Storage blob container for deleted blobs. </summary>
        private BlobContainerClient Deleted { get; }

        public async Task<BlobClient> GetBlob()
        {
            var chosen = await Chosen(CancellationToken.None).ConfigureAwait(false);
            return chosen.Blob;
        }

        /// <summary>
        /// Chose the blob where readind is performed, start copy if the NewBlob does not exist.
        /// </summary>
        /// <param name="cancel"></param>
        /// <returns></returns>
        private async Task<AzureBlobRef> Chosen(CancellationToken cancel)
        {
            if (_chosen != null) return _chosen;
            _chosen = new AzureBlobRef(Realm, Hash, NewBlob, Deleted);
            if (await AzureRetry.Do(NewBlob.ExistsAsync, cancel).ConfigureAwait(false))
            {
                var props = await AzureRetry.Do(
                            c => NewBlob.GetPropertiesAsync(cancellationToken: c),
                            cancel).ConfigureAwait(false);

                switch (props.Value.BlobCopyStatus)
                {
                    case CopyStatus.Aborted:
                    case CopyStatus.Failed:
                        if (await AzureRetry.Do(OldBlob.ExistsAsync, cancel).ConfigureAwait(false))
                        {
                            _ = StartCopy(cancel);
                            _chosen = new AzureBlobRef(Realm, Hash, OldBlob, Deleted);
                        }
                        break;
                    case CopyStatus.Pending:
                        if (await AzureRetry.Do(OldBlob.ExistsAsync, cancel).ConfigureAwait(false))
                        {
                            _chosen = new AzureBlobRef(Realm, Hash, OldBlob, Deleted);
                        }
                        break;
                    case CopyStatus.Success:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            else
            {
                if (await AzureRetry.Do(OldBlob.ExistsAsync, cancel).ConfigureAwait(false))
                {
                    _ = StartCopy(cancel);
                    _chosen = new AzureBlobRef(Realm, Hash, OldBlob, Deleted);
                }
            }

            return _chosen;
        }

        public async Task StartCopy(CancellationToken cancel)
        {
            var oldBlobRef = new AzureBlobRef(Realm, Hash, OldBlob, Deleted);
            if (oldBlobRef.Blob.CanGenerateSasUri)
            {
                // If possible, generate a download URL and use that as the copy
                // source.
                var oldUri = await oldBlobRef.GetDownloadUrlAsync(
                    TimeSpan.FromDays(1),
                    "",
                    "",
                    cancel).ConfigureAwait(false);

                await AzureRetry.Do(c => NewBlob.StartCopyFromUriAsync(oldUri, cancellationToken: c), cancel);
            }
            else
            {
                // SAS-based access (such as read-only stores) cannot be used to 
                // generate an URL, so we instead use the copy-from-blob primitive, 
                // which is mostly equivalent (but can cause internal errors on the Azure
                // side). 
                await AzureRetry.Do(c => NewBlob.StartCopyFromUriAsync(oldBlobRef.Blob.Uri, cancellationToken: c), cancel);
            }
        }

        /// <see cref="IReadBlobRef.ExistsAsync"/>
        public async Task<bool> ExistsAsync(CancellationToken cancel)
        {
            var chosen = await Chosen(cancel).ConfigureAwait(false);
            return await chosen.ExistsAsync(cancel).ConfigureAwait(false);
        }


        /// <see cref="IReadBlobRef.GetSizeAsync"/>
        public async Task<long> GetSizeAsync(CancellationToken cancel)
        {
            var chosen = await Chosen(cancel).ConfigureAwait(false);
            return await chosen.GetSizeAsync(cancel).ConfigureAwait(false);
        }

        /// <see cref="IReadBlobRef.OpenAsync"/>
        public async Task<Stream> OpenAsync(CancellationToken cancel)
        {
            var chosen = await Chosen(cancel).ConfigureAwait(false);
            return await chosen.OpenAsync(cancel).ConfigureAwait(false);
        }

        /// <see cref="IAzureReadBlobRef.GetDownloadUrlAsync"/> 
        public async Task<Uri> GetDownloadUrlAsync(
            DateTime now,
            TimeSpan life,
            string filename,
            string contentType,
            CancellationToken cancel)
        {
            var chosen = await Chosen(cancel).ConfigureAwait(false);
            return await chosen.GetDownloadUrlAsync(
                now,
                life,
                filename,
                contentType,
                cancel).ConfigureAwait(false);
        }
    }
}
