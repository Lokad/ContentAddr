using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary>
    ///     Writes content to a temporary S3 object using either single-part PutObject or multipart uploads.
    /// </summary>
    /// <remarks>
    ///     This class is intentionally focused on staging data only. The caller is responsible for
    ///     committing or promoting the temporary object to its final key.
    ///
    ///     Upload strategy:
    ///     - Data is buffered in-memory until it reaches the S3 multipart minimum part size (5 MB).
    ///     - Full 5 MB parts are uploaded via multipart as they become available.
    ///     - If the total payload is still smaller than 5 MB at commit time, a single PutObject is used.
    ///     - If multipart has started, any remaining tail (which may be &lt; 5 MB) is uploaded as the final part.
    ///
    ///     The buffer is not thread-safe; callers should not write concurrently.
    /// </remarks>
    public abstract class S3PreWriter : StoreWriter
    {
        private readonly string _realm;
        private readonly OnStagingDataSent _onStagingDataSent;

        /// <summary>S3 client used for all upload operations.</summary>
        protected IAmazonS3 Client { get; }
        /// <summary>Bucket containing the temporary object.</summary>
        protected string Bucket { get; }
        /// <summary>Key for the temporary object being built.</summary>
        protected string TemporaryKey { get; }
        /// <summary>Total number of bytes written to the temporary object.</summary>
        protected long TemporarySize { get; private set; }

        private readonly Func<Task<string>> _uploadIdFactory;
        private Task<string> _uploadIdTask;
        private string _uploadId;
        /// <remarks>
        ///     Invariant: this list is append-only until completion, and its order defines part numbers:
        ///     partNumber = index + 1. Do not filter, reorder, or clear before completion.
        /// </remarks>
        private readonly List<Task<PartETag>> _tasks = new List<Task<PartETag>>();
        // In-memory staging buffer. Data is accumulated until it can form full multipart parts.
        private readonly MemoryStream _buffer = new MemoryStream();
        /// <summary>
        ///     Creates a pre-writer that will stage data into a temporary S3 object.
        /// </summary>
        /// <param name="realm">Store realm id that owns the staging blob.</param>
        /// <param name="client">S3 client instance.</param>
        /// <param name="bucket">Target bucket name.</param>
        /// <param name="temporaryKey">Temporary object key.</param>
        /// <param name="onStagingDataSent">Callback invoked when bytes are uploaded to the temporary object.</param>
        /// <param name="uploadIdFactory">Factory to initiate multipart uploads when needed.</param>
        protected S3PreWriter(
            string realm,
            IAmazonS3 client,
            string bucket,
            string temporaryKey,
            OnStagingDataSent onStagingDataSent,
            Func<Task<string>> uploadIdFactory)
        {
            _realm = realm ?? throw new ArgumentNullException(nameof(realm));
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
            TemporaryKey = temporaryKey ?? throw new ArgumentNullException(nameof(temporaryKey));
            _onStagingDataSent = onStagingDataSent;
            _uploadIdFactory = uploadIdFactory ?? throw new ArgumentNullException(nameof(uploadIdFactory));
        }

        /// <summary>
        ///     Minimum part size enforced by S3-compatible multipart uploads (5 MB).
        /// </summary>
        private const int MultipartMinPartSize = 5 * 1024 * 1024;

        /// <summary>
        /// Called when bytes are uploaded to a staging blob.
        /// </summary>
        /// <param name="realm">Realm id.</param>
        /// <param name="stagingBlobReference">Staging blob reference (full temporary S3 key).</param>
        /// <param name="bytes">Uploaded byte count.</param>
        public delegate void OnStagingDataSent(string realm, string stagingBlobReference, long bytes);

        /// <summary>
        ///     Lazily initializes and caches the multipart upload id.
        /// </summary>
        private async Task<string> EnsureUploadIdAsync()
        {
            if (_uploadId != null) return _uploadId;
            _uploadIdTask ??= _uploadIdFactory();
            _uploadId = await _uploadIdTask.ConfigureAwait(false);
            return _uploadId;
        }

        /// <summary>
        ///     Uploads a single multipart part and returns its ETag for completion.
        /// </summary>
        private async Task<PartETag> UploadPartAsync(int partNumber, ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            var uploadId = await EnsureUploadIdAsync().ConfigureAwait(false);
            using var stream = ReadOnlyMemoryStream.Create(buffer);

            var request = new UploadPartRequest
            {
                BucketName = Bucket,
                Key = TemporaryKey,
                UploadId = uploadId,
                PartNumber = partNumber,
                PartSize = buffer.Length,
                InputStream = stream
            };

            var response = await Client.UploadPartAsync(request, cancel).ConfigureAwait(false);
            _onStagingDataSent?.Invoke(_realm, TemporaryKey, buffer.Length);

            return new PartETag(partNumber, response.ETag);
        }

        /// <summary>
        ///     Buffers data and uploads full multipart parts when possible.
        /// </summary>
        protected override Task DoWriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
        {
            if (buffer.Length == 0) return Task.CompletedTask;

            TemporarySize += buffer.Length;
            _buffer.Write(buffer.Span);
            return FlushFullPartsAsync(cancel);
        }

        /// <summary>
        ///     Uploads as many full-size multipart parts as are available in the buffer.
        /// </summary>
        private Task FlushFullPartsAsync(CancellationToken cancel)
        {
            if (_buffer.Length < MultipartMinPartSize) return Task.CompletedTask;

            var data = _buffer.ToArray();
            var tasks = new List<Task>();
            var offset = 0;

            while (data.Length - offset >= MultipartMinPartSize)
            {
                var partNumber = _tasks.Count + 1;
                var part = new ReadOnlyMemory<byte>(data, offset, MultipartMinPartSize);
                var task = UploadPartAsync(partNumber, part, cancel);
                _tasks.Add(task);
                tasks.Add(task);
                offset += MultipartMinPartSize;
            }

            _buffer.SetLength(0);
            if (data.Length - offset > 0)
                _buffer.Write(data, offset, data.Length - offset);

            return tasks.Count == 0 ? Task.CompletedTask : Task.WhenAll(tasks);
        }

        /// <summary>
        ///     Writes any buffered data to the temporary object and completes multipart uploads if needed.
        /// </summary>
        protected async Task WriteTemporary(CancellationToken cancel)
        {
            if (_buffer.Length > 0)
            {
                // If no multipart has started and the total payload is small, use PutObject.
                if (_tasks.Count == 0 && _buffer.Length < MultipartMinPartSize)
                {
                    using var input = new MemoryStream(_buffer.ToArray());
                    await Client.PutObjectAsync(new PutObjectRequest
                    {
                        BucketName = Bucket,
                        Key = TemporaryKey,
                        InputStream = input
                    }, cancel).ConfigureAwait(false);
                    _onStagingDataSent?.Invoke(_realm, TemporaryKey, input.Length);
                    _buffer.SetLength(0);
                    return;
                }

                // Multipart is required or already in progress: upload the remaining tail as the final part.
                var remaining = _buffer.ToArray();
                var partNumber = _tasks.Count + 1;
                var task = UploadPartAsync(partNumber, remaining, cancel);
                _tasks.Add(task);
                _buffer.SetLength(0);
            }

            // Empty payload, or caller never wrote anything.
            if (_tasks.Count == 0)
            {
                using var empty = new MemoryStream(Array.Empty<byte>());
                await Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = Bucket,
                    Key = TemporaryKey,
                    InputStream = empty
                }, cancel).ConfigureAwait(false);
                _onStagingDataSent?.Invoke(_realm, TemporaryKey, 0);
                return;
            }

            var parts = await Task.WhenAll(_tasks).ConfigureAwait(false);

            var uploadId = await EnsureUploadIdAsync().ConfigureAwait(false);

            Array.Sort(parts, (a, b) => a.PartNumber.CompareTo(b.PartNumber));

            var completeRequest = new CompleteMultipartUploadRequest
            {
                BucketName = Bucket,
                Key = TemporaryKey,
                UploadId = uploadId,
                PartETags = parts.ToList()
            };

            await Client.CompleteMultipartUploadAsync(completeRequest, cancel).ConfigureAwait(false);
        }
    }
}
