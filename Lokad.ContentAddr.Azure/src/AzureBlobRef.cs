using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> The <see cref="IReadBlobRef"/> for an azure persistent store. </summary>
    /// <see cref="AzureReadOnlyStore"/>
    /// <remarks>
    ///     Exposes the Azure Storage blob itself in <see cref="Blob"/>.
    /// </remarks>
    public sealed class AzureBlobRef : IAzureReadBlobRef
    {
        public AzureBlobRef(string realm, Hash hash, BlobClient blob, BlobContainerClient deleted)
        {
            Hash = hash;
            Realm = realm;
            Blob = blob;
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

        /// <summary> The Azure Storage blob where the blob data is stored. </summary>
        public BlobClient Blob { get; }

        /// <summary> The Azure Storage blob container for deleted blobs. </summary>
        private BlobContainerClient Deleted { get; }

        /// <see cref="IReadBlobRef.ExistsAsync"/>
        public async Task<bool> ExistsAsync(CancellationToken cancel) =>
            await Blob.ExistsAsync(cancel);

        /// <see cref="IAzureReadBlobRef.GetBlob"/>
        public Task<BlobClient> GetBlob() =>
            Task.FromResult(Blob);

        /// <see cref="IReadBlobRef.GetSizeAsync"/>
        public async Task<long> GetSizeAsync(CancellationToken cancel)
        {
            Response<BlobProperties> props;
            try 
            {
                props = await Blob.GetPropertiesAsync(cancellationToken: cancel);
            }
            catch (RequestFailedException e) when (e.Status == 404)
            {
                throw await ReadDeletedBlobAsync(this.Deleted, this.Realm, this.Hash, cancel).ConfigureAwait(false);
            }

            return props.Value.ContentLength;
        }

        /// <see cref="IReadBlobRef.OpenAsync"/>
        public async Task<Stream> OpenAsync(CancellationToken cancel)
        {
            var size = await GetSizeAsync(cancel).ConfigureAwait(false);
            return new AzureReadStream(Blob, size);
        }

        /// <see cref="IAzureReadBlobRef.GetDownloadUrlAsync"/>
        public Task<Uri> GetDownloadUrlAsync(
            DateTime now,
            TimeSpan life,
            string filename,
            string contentType,
            CancellationToken cancel)
        {
            var (asciiFilename, utf8Filename) = SanitizeFileName(filename);

            // This Content-Disposition header will be returned by Azure along with the blob.
            // It is constructed according to RFC6266
            // https://tools.ietf.org/html/rfc6266
            //
            // If the file name is ASCII-only, we use the standard for `attachment;filename="foo.tsv"`
            // which will cause the browser to save (because `attachment`) the blob as a file 
            // named `foo.tsv`.
            //
            // If the file name contains non-ASCII characters, we include the `filename=` as an
            // ASCII-only fallback for older browsers (but it will be something like `data.tsv` 
            // instead of the actual file name), and the `filename*=UTF-8''f%c3%a4.tsv` for modern
            // browsers, encoding according to RFC5987
            // https://tools.ietf.org/html/rfc5987#section-3.2

            var contentDisposition =
                utf8Filename != null
                    ? "attachment;filename=\"" + asciiFilename + "\";filename*=UTF-8''" + utf8Filename + ""
                    : "attachment;filename=\"" + asciiFilename + "\"";

            var token = Blob.GenerateSasUri(new BlobSasBuilder(BlobContainerSasPermissions.Read, new DateTimeOffset(now + life))
            {
                StartsOn = new DateTimeOffset(now.AddMinutes(-5)),
                ContentDisposition = contentDisposition,
                ContentType = contentType,
                ContentEncoding = null,
            });
            return Task.FromResult(new Uri(Blob.Uri, token));
        }

        /// <summary> Recognizes characters that will be replaced with a single '-'. </summary>
        private static readonly Regex BadFilenameCharacter =
            new Regex("[\\x00-\\x1F\\x7F/\\\\?%*:|*\"<>-]+", RegexOptions.Compiled);

        /// <summary> Sanitizes a file name for the content-disposition header. </summary>
        /// <remarks>
        ///     Empty file names are replaced with `"data"`.
        ///     
        ///     Extension-only file names (like `".tsv"`) are prepended with `"data"` 
        ///     (for example `"data.tsv"`)
        ///     
        ///     Any characters that are not allowed in file names <see cref="BadFilenameCharacter"/>
        ///     are replaced with `-` and consecutive `-` are combined into one. 
        ///     
        ///     Any initial or final `-` or `.` are dropped. 
        ///     
        ///     If the string contains any non-ASCII characters, it will return a dummy `data.ext`
        ///     ASCII filename (keeping only the extension) and an UTF8 filename properly 
        ///     encoded according to RFC5987 (without the preceding `UTF-8''`)
        ///     
        ///     https://tools.ietf.org/html/rfc5987#section-3.2
        ///     
        ///     If only ASCII characters are present, the returned UTF-8 filename will 
        ///     be null.
        /// </remarks>
        public static (string ascii, string utf8) SanitizeFileName(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || filename[0] == '.')
                filename = "data" + filename;

            filename = BadFilenameCharacter.Replace(filename, "-").Trim('-', '.');

            if (filename == "")
                return ("data", null);

            if (filename.All(c => c < 127))
                return (filename, null);

            // Found UTF8 characters.

            var utf8 = new StringBuilder();
            var bytes = Encoding.UTF8.GetBytes(filename);
            foreach (var b in bytes)
            {
                if (b >= 'a' && b <= 'z' ||
                    b >= 'A' && b <= 'Z' ||
                    b >= '0' && b <= '9' ||
                    b < 127 && (
                        b == '!' ||
                        b == '#' ||
                        b == '$' ||
                        b == '+' ||
                        b == '-' ||
                        b == '.' ||
                        b == '^' ||
                        b == '_' ||
                        b == '`' ||
                        b == '|' ||
                        b == '~'))
                {
                    // Characters explicitly allowed by RFC5987 may be kept verbatim,
                    // all others are %-escaped.
                    utf8.Append((char)b);
                }
                else
                {
                    utf8.Append($"%{b:x2}");
                }
            }

            var ext = Path.GetExtension(filename) ?? ".bin";
            if (ext == ".gz") ext = ".csv.gz";

            return ("data" + ext, utf8.ToString());
        }

        /// <summary>
        /// When failing to find a blob in Persistent, we search with the same realm and hash in the blob container Deleted <see cref="IAzureStore.DeleteWithReasonAsync"/>
        /// If we find it, it means we saved data about the blob deletion, 
        /// A specific exception <see cref="AzureDeletedBlobException"/> that contains <see cref="AzureDeletedBlobInfo"/> is thrown
        /// </summary>
        /// <param name="deleted"> The blob container for deleted blobs. </param>
        /// <param name="realm"> The blob name prefix. </param>
        /// <param name="hash"> The hash of the archived blob to be deleted. </param>
        /// <param name="cancel"> Cancellation token. </param>
        /// <returns><see cref="AzureDeletedBlobException"/> or <see cref="NoSuchBlobException"/> if a blob exists in deleted. </returns>
        public static async Task<Exception> ReadDeletedBlobAsync(BlobContainerClient deleted, string realm, Hash hash, CancellationToken cancel)
        {
            if (deleted == null)
                return new NoSuchBlobException(realm, hash);

            try
            {
                var blobDeleted = deleted.GetBlobClient(AzureReadOnlyStore.AzureBlobName(realm, hash));

                var download = (await blobDeleted.DownloadContentAsync(cancel)).Value;

                var result = download.Content.ToArray();

                return new AzureDeletedBlobException(
                    JsonConvert.DeserializeObject<AzureDeletedBlobInfo>(
                        Encoding.UTF8.GetString(result)),
                    realm,
                    hash);
            }
            catch (RequestFailedException e) when (e.Status == 404)
            {
                return new NoSuchBlobException(realm, hash);
            }
        }
    }
}