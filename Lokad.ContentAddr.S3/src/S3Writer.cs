using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary>
    ///     Uploads data to a temporary S3 object,
    ///     then copies it over to the permanent content-addressed object.
    /// </summary>
    public sealed class S3Writer : S3PreWriter
    {
        private readonly string _realm;
        private readonly string _persistPrefix;
        private readonly OnCommit _onCommit;
        private readonly Stopwatch _stopwatch;

        public S3Writer(
            string realm,
            IAmazonS3 client,
            string bucket,
            string persistPrefix,
            string temporaryKey,
            OnCommit onCommit,
            Func<Task<string>> uploadIdFactory) : base(client, bucket, temporaryKey, uploadIdFactory)
        {
            _realm = realm;
            _persistPrefix = persistPrefix;
            _onCommit = onCommit;
            _stopwatch = Stopwatch.StartNew();
        }

        protected override Task DoCommitAsync(Hash hash, CancellationToken cancel) =>
            DoOptCommitAsync(hash, null, cancel);

        protected override async Task DoOptCommitAsync(Hash hash, Func<Task> optionalWrite, CancellationToken cancel)
        {
            var finalKey = S3ReadOnlyStore.PersistentObjectKey(_persistPrefix, _realm, hash);

            if (await S3Store.ObjectExistsAsync(Client, Bucket, finalKey, cancel).ConfigureAwait(false))
            {
                var metadata = await Client.GetObjectMetadataAsync(Bucket, finalKey, cancel).ConfigureAwait(false);
                _onCommit?.Invoke(_stopwatch.Elapsed, _realm, hash, metadata.ContentLength, true);
                return;
            }

            if (optionalWrite != null) await optionalWrite().ConfigureAwait(false);
            await WriteTemporary(cancel).ConfigureAwait(false);

            try
            {
                await S3Store.CopyToPersistent(Client, Bucket, TemporaryKey, finalKey, cancel).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new BlobCommitException($"s3://{Bucket}/{TemporaryKey}", $"s3://{Bucket}/{finalKey}", ex);
            }
            finally
            {
                S3Store.DeleteObjectAfterDelay(Client, Bucket, TemporaryKey, TimeSpan.FromSeconds(1));
            }

            var props = await Client.GetObjectMetadataAsync(Bucket, finalKey, cancel).ConfigureAwait(false);
            _onCommit?.Invoke(_stopwatch.Elapsed, _realm, hash, props.ContentLength, false);
        }

        public delegate void OnCommit(TimeSpan elapsed, string realm, Hash hash, long size, bool alreadyExists);
    }
}
