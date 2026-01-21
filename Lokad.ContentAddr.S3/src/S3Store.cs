using Amazon.S3;
using Amazon.S3.Model;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> Persistent content-addressable store backed by S3-compatible storage. </summary>
    /// <see cref="S3ReadOnlyStore"/>
    /// <remarks>
    ///     Supports uploading blobs, as well as committing blobs uploaded to a staging
    ///     prefix.
    /// </remarks>
    public sealed class S3Store : S3ReadOnlyStore, IS3Store
    {
        private readonly S3Writer.OnCommit _onCommit;
        private readonly string _stagingPrefix;

        public S3Store(
            string realm,
            IAmazonS3 client,
            string bucket,
            string persistPrefix,
            string stagingPrefix,
            string deletedPrefix,
            S3Writer.OnCommit onCommit = null)
            : base(realm, client, bucket, persistPrefix, deletedPrefix)
        {
            _onCommit = onCommit;
            _stagingPrefix = NormalizePrefix(stagingPrefix);
        }

        public StoreWriter StartWriting()
        {
            var tempKey = TempObjectKey();
            return new S3Writer(Realm, Client, Bucket, PersistPrefix, tempKey, _onCommit, () => InitiateMultipartUploadAsync(tempKey));
        }

        private string TempObjectKey() =>
            $"{_stagingPrefix}{DateTime.UtcNow:yyyy-MM-dd}/{Realm}/{Guid.NewGuid()}";

        private async Task<string> InitiateMultipartUploadAsync(string key)
        {
            var response = await Client.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
            {
                BucketName = Bucket,
                Key = key
            }).ConfigureAwait(false);

            return response.UploadId;
        }

        public async Task<IS3ReadBlobRef> CommitTemporaryBlob(string name, CancellationToken cancel)
        {
            var sw = Stopwatch.StartNew();
            var tempKey = name;

            if (!await ObjectExistsAsync(Client, Bucket, tempKey, cancel).ConfigureAwait(false))
                throw new CommitBlobException(Realm, name, "temporary object does not exist.");

            var md5 = MD5.Create();
            var metadata = await Client.GetObjectMetadataAsync(Bucket, tempKey, cancel).ConfigureAwait(false);
            var bufferSize = 4 * 1024 * 1024;

            if (metadata.ContentLength < bufferSize)
                bufferSize = (int)metadata.ContentLength;

            var buffer = new byte[bufferSize];
            long position = 0;

            using (var response = await Client.GetObjectAsync(Bucket, tempKey, cancel).ConfigureAwait(false))
            using (var stream = response.ResponseStream)
            {
                int read;
                do
                {
                    read = await stream.ReadAsync(buffer, 0, bufferSize, cancel).ConfigureAwait(false);
                    position += read;
                    if (read > 0)
                        md5.TransformBlock(buffer, 0, read, buffer, 0);
                } while (read > 0);
            }

            md5.TransformFinalBlock(buffer, 0, 0);
            var hash = new Hash(md5.Hash);
            var finalKey = PersistentObjectKey(PersistPrefix, Realm, hash);

            try
            {
                var exists = await ObjectExistsAsync(Client, Bucket, finalKey, cancel).ConfigureAwait(false);
                if (!exists)
                {
                    await CopyToPersistent(Client, Bucket, tempKey, finalKey, cancel).ConfigureAwait(false);
                }

                var finalMeta = await Client.GetObjectMetadataAsync(Bucket, finalKey, cancel).ConfigureAwait(false);
                _onCommit?.Invoke(sw.Elapsed, Realm, hash, finalMeta.ContentLength, exists);
            }
            finally
            {
                DeleteObjectAfterDelay(Client, Bucket, tempKey, TimeSpan.FromMinutes(10));
            }

            return new S3BlobRef(Realm, hash, Client, Bucket, finalKey, DeletedPrefix);
        }

        public async Task DeleteWithReasonAsync(Hash hash, string reason, CancellationToken cancel)
        {
            var objectKey = PersistentObjectKey(PersistPrefix, Realm, hash);
            var deletedKey = DeletedObjectKey(DeletedPrefix, Realm, hash);

            S3DeletedBlobInfo deletedInfo;
            try
            {
                var props = await Client.GetObjectMetadataAsync(Bucket, objectKey, cancel).ConfigureAwait(false);
                deletedInfo = new S3DeletedBlobInfo
                {
                    Created = props.LastModified.ToUniversalTime(),
                    Deleted = DateTime.UtcNow,
                    Reason = reason,
                    Size = props.ContentLength
                };
            }
            catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound || e.ErrorCode == "NoSuchKey")
            {
                throw await S3BlobRef.ReadDeletedBlobAsync(Client, Bucket, DeletedPrefix, Realm, hash, cancel).ConfigureAwait(false);
            }

            var json = JsonConvert.SerializeObject(deletedInfo);
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                await Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = Bucket,
                    Key = deletedKey,
                    InputStream = ms
                }, cancel).ConfigureAwait(false);
            }

            await Client.DeleteObjectAsync(Bucket, objectKey, cancel).ConfigureAwait(false);
        }

        public static void DeleteObjectAfterDelay(IAmazonS3 client, string bucket, string key, TimeSpan wait)
        {
            Task.Delay(wait).ContinueWith(_ => client.DeleteObjectAsync(bucket, key));
        }

        public static async Task CopyToPersistent(
            IAmazonS3 client,
            string bucket,
            string sourceKey,
            string destinationKey,
            CancellationToken cancel)
        {
            await S3Retry.Do(
                async c =>
                {
                    try
                    {
                        await client.CopyObjectAsync(new CopyObjectRequest
                        {
                            SourceBucket = bucket,
                            SourceKey = sourceKey,
                            DestinationBucket = bucket,
                            DestinationKey = destinationKey
                        }, c).ConfigureAwait(false);
                    }
                    catch
                    {
                        if (!await ObjectExistsAsync(client, bucket, destinationKey, cancel).ConfigureAwait(false))
                            throw;
                    }
                },
                cancel).ConfigureAwait(false);
        }

        public static async Task<bool> ObjectExistsAsync(
            IAmazonS3 client,
            string bucket,
            string key,
            CancellationToken cancel)
        {
            return await S3Retry.OrFalse(async () =>
            {
                try
                {
                    await client.GetObjectMetadataAsync(bucket, key, cancel).ConfigureAwait(false);
                    return true;
                }
                catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound || e.ErrorCode == "NoSuchKey")
                {
                    return false;
                }
            }).ConfigureAwait(false);
        }
    }
}
