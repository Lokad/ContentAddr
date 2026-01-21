using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Newtonsoft.Json;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> A readable blob reference in a store.</summary>
    /// <remarks> The blob does not necessarily exist. </remarks>
    public interface IAzureReadBlobRef : IReadBlobRef
    {
        /// <summary>
        /// Returns the actual storage blob
        /// </summary>
        Task<BlobClient> GetBlob();

        /// <summary> The realm of this blob. </summary>
        string Realm { get; }

        /// <summary> Returns a publicly accessible temporary download URL. </summary>
        /// <remarks> Will not check whether the blob exists. </remarks>
        /// <param name="now"> The current time. </param>
        /// <param name="life"> How long should the temporary URL last ? </param>
        /// <param name="filename"> The name of the file (in the <c>Content-Disposition</c> header). </param>
        /// <param name="contentType"> The <c>Content-Type</c> header. </param>
        /// <param name="cancel"> Cancellation token. </param>
        Task<Uri> GetDownloadUrlAsync(
            DateTime now,
            TimeSpan life,
            string filename,
            string contentType,
            CancellationToken cancel);
    }

    public static class AzureReadBlobRefExtensions
    {
        /// <summary> Returns a publicly accessible temporary download URL. </summary>
        /// <exception cref="NoSuchBlobException"> If the blob does not exist. </exception>
        /// <param name="blob"> The blob to be downloaded. </param>
        /// <param name="life"> How long should the temporary URL last ? </param>
        /// <param name="filename"> The name of the file (in the <c>Content-Disposition</c> header). </param>
        /// <param name="contentType"> The <c>Content-Type</c> header. </param>
        /// <param name="cancel"> Cancellation token. </param>
        public static async Task<Uri> GetDownloadUrlAsync(
            this IAzureReadBlobRef blob,
            TimeSpan life,
            string filename,
            string contentType,
            CancellationToken cancel)
        {
            if (!await blob.ExistsAsync(cancel).ConfigureAwait(false))
                // Handle the choice of exception
                _ = await blob.GetSizeAsync(cancel).ConfigureAwait(false);

            return await blob.GetDownloadUrlAsync(
                DateTime.UtcNow,
                life,
                filename,
                contentType,
                cancel).ConfigureAwait(false);
        }
    }
}