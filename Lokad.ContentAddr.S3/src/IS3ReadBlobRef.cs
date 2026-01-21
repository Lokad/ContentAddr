using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> A readable blob reference in a store.</summary>
    /// <remarks> The blob does not necessarily exist. </remarks>
    public interface IS3ReadBlobRef : IReadBlobRef
    {
        /// <summary> The bucket hosting this blob. </summary>
        string Bucket { get; }

        /// <summary> The object key for this blob. </summary>
        string Key { get; }

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

    public static class S3ReadBlobRefExtensions
    {
        /// <summary> Returns a publicly accessible temporary download URL. </summary>
        /// <exception cref="NoSuchBlobException"> If the blob does not exist. </exception>
        /// <param name="blob"> The blob to be downloaded. </param>
        /// <param name="life"> How long should the temporary URL last ? </param>
        /// <param name="filename"> The name of the file (in the <c>Content-Disposition</c> header). </param>
        /// <param name="contentType"> The <c>Content-Type</c> header. </param>
        /// <param name="cancel"> Cancellation token. </param>
        public static async Task<Uri> GetDownloadUrlAsync(
            this IS3ReadBlobRef blob,
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
