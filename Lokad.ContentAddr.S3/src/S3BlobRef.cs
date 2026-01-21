using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> The <see cref="IReadBlobRef"/> for an S3 persistent store. </summary>
    /// <see cref="S3ReadOnlyStore"/>
    public sealed class S3BlobRef : IS3ReadBlobRef
    {
        private readonly IAmazonS3 _client;
        private readonly string _deletedPrefix;

        public S3BlobRef(string realm, Hash hash, IAmazonS3 client, string bucket, string key, string deletedPrefix)
        {
            Hash = hash;
            Realm = realm;
            _client = client ?? throw new ArgumentNullException(nameof(client));
            Bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
            Key = key ?? throw new ArgumentNullException(nameof(key));
            _deletedPrefix = deletedPrefix ?? throw new ArgumentNullException(nameof(deletedPrefix));
        }

        public Hash Hash { get; }

        /// <summary> The bucket hosting this blob. </summary>
        public string Bucket { get; }

        /// <summary> The object key for this blob. </summary>
        public string Key { get; }

        /// <summary> The blob name prefix (realm). </summary>
        public string Realm { get; }

        public async Task<bool> ExistsAsync(CancellationToken cancel) =>
            await S3Store.ObjectExistsAsync(_client, Bucket, Key, cancel).ConfigureAwait(false);

        public async Task<long> GetSizeAsync(CancellationToken cancel)
        {
            try
            {
                var metadata = await _client.GetObjectMetadataAsync(Bucket, Key, cancel).ConfigureAwait(false);
                return metadata.ContentLength;
            }
            catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound || e.ErrorCode == "NoSuchKey")
            {
                throw await ReadDeletedBlobAsync(_client, Bucket, _deletedPrefix, Realm, Hash, cancel).ConfigureAwait(false);
            }
        }

        public async Task<Stream> OpenAsync(CancellationToken cancel)
        {
            var size = await GetSizeAsync(cancel).ConfigureAwait(false);
            return new S3ReadStream(_client, Bucket, Key, size);
        }

        public Task<Uri> GetDownloadUrlAsync(
            DateTime now,
            TimeSpan life,
            string filename,
            string contentType,
            CancellationToken cancel)
        {
            var (asciiFilename, utf8Filename) = SanitizeFileName(filename);

            var contentDisposition =
                utf8Filename != null
                    ? "attachment;filename=\"" + asciiFilename + "\";filename*=UTF-8''" + utf8Filename + ""
                    : "attachment;filename=\"" + asciiFilename + "\"";

            var request = new GetPreSignedUrlRequest
            {
                BucketName = Bucket,
                Key = Key,
                Expires = now + life,
                Verb = HttpVerb.GET,
                ResponseHeaderOverrides = new ResponseHeaderOverrides
                {
                    ContentDisposition = contentDisposition,
                    ContentType = contentType
                }
            };

            var url = _client.GetPreSignedURL(request);
            return Task.FromResult(new Uri(url));
        }

        private static readonly Regex BadFilenameCharacter =
            new Regex("[\\x00-\\x1F\\x7F/\\\\?%*:|*\"<>-]+", RegexOptions.Compiled);

        public static (string ascii, string utf8) SanitizeFileName(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename) || filename[0] == '.')
                filename = "data" + filename;

            filename = BadFilenameCharacter.Replace(filename, "-").Trim('-', '.');

            if (filename == "")
                return ("data", null);

            if (filename.All(c => c < 127))
                return (filename, null);

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
        /// When failing to find a blob in Persistent, we search with the same realm and hash in deleted prefix.
        /// A specific exception <see cref="S3DeletedBlobException"/> that contains <see cref="S3DeletedBlobInfo"/> is thrown.
        /// </summary>
        /// <returns><see cref="S3DeletedBlobException"/> or <see cref="NoSuchBlobException"/> if a blob exists in deleted. </returns>
        public static async Task<Exception> ReadDeletedBlobAsync(
            IAmazonS3 client,
            string bucket,
            string deletedPrefix,
            string realm,
            Hash hash,
            CancellationToken cancel)
        {
            if (string.IsNullOrWhiteSpace(deletedPrefix))
                return new NoSuchBlobException(realm, hash);

            try
            {
                var key = S3ReadOnlyStore.DeletedObjectKey(deletedPrefix, realm, hash);
                using var response = await client.GetObjectAsync(bucket, key, cancel).ConfigureAwait(false);
                using var stream = response.ResponseStream;
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, cancel).ConfigureAwait(false);

                return new S3DeletedBlobException(
                    JsonConvert.DeserializeObject<S3DeletedBlobInfo>(
                        Encoding.UTF8.GetString(ms.ToArray())),
                    realm,
                    hash);
            }
            catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound || e.ErrorCode == "NoSuchKey")
            {
                return new NoSuchBlobException(realm, hash);
            }
        }
    }
}
