using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> Generates <see cref="S3Store"/> instances for specific accounts. </summary>
    public sealed class S3StoreFactory : IS3StoreFactory
    {
        private readonly string _persistPrefix;
        private readonly string _stagingPrefix;
        private readonly string _deletedPrefix;
        private readonly string _testPrefix;
        private readonly string _bucket;

        public S3Writer.OnCommit OnCommit { get; set; }

        public IAmazonS3 Client { get; }

        public static IS3StoreFactory ParseConfig(string config, bool readOnly = false, string testPrefix = null) =>
            new S3StoreFactory(config, readOnly, testPrefix);

        public S3StoreFactory(string config, bool readOnly = false, string testPrefix = null)
            : this(ParseSettings(config), readOnly, testPrefix)
        { }

        public S3StoreFactory(S3ConnectionSettings settings, bool readOnly = false, string testPrefix = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.Bucket))
                throw new ArgumentException("Missing Bucket in S3 settings.");

            _bucket = settings.Bucket;
            _testPrefix = testPrefix;

            _persistPrefix = testPrefix == null ? "persist" : testPrefix + "-persist";
            _stagingPrefix = testPrefix == null ? "staging" : testPrefix + "-staging";
            _deletedPrefix = testPrefix == null ? "deleted" : testPrefix + "-deleted";

            Client = BuildClient(settings);

            if (!readOnly)
            {
                EnsureBucketExists(Client, _bucket).GetAwaiter().GetResult();
            }
            else
            {
                if (!AmazonS3Util.DoesS3BucketExistV2Async(Client, _bucket).GetAwaiter().GetResult())
                    throw new Exception("Cannot access bucket in read-only mode.");
            }
        }

        public IS3Store ForAccount(long account)
        {
            return new S3Store(
                account.ToString(CultureInfo.InvariantCulture),
                Client,
                _bucket,
                _persistPrefix,
                _stagingPrefix,
                _deletedPrefix,
                OnCommit);
        }

        public IStore<IReadBlobRef> this[long account] => ForAccount(account);

        public IReadOnlyStore<IReadBlobRef> ReadOnlyStore(long account) =>
            ReadOnlyForAccount(account);

        public IS3ReadOnlyStore ReadOnlyForAccount(long account) =>
            new S3ReadOnlyStore(
                account.ToString(CultureInfo.InvariantCulture),
                Client,
                _bucket,
                _persistPrefix,
                _deletedPrefix);

        public void Delete()
        {
            if (_testPrefix == null)
                throw new InvalidOperationException("Cannot delete non-test persistent store.");

            DeletePrefix(_persistPrefix).GetAwaiter().GetResult();
            DeletePrefix(_stagingPrefix).GetAwaiter().GetResult();
            DeletePrefix(_deletedPrefix).GetAwaiter().GetResult();
        }

        public string Describe() => "[CAS] s3://" + _bucket + "/" + _persistPrefix;

        public async Task<IReadOnlyList<long>> GetAccountsAsync(CancellationToken cancel)
        {
            var accounts = new HashSet<long>();
            var prefix = NormalizePrefix(_persistPrefix);
            string continuationToken = null;

            do
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = prefix,
                    Delimiter = "/",
                    ContinuationToken = continuationToken
                };

                var response = await Client.ListObjectsV2Async(request, cancel).ConfigureAwait(false);
                foreach (var commonPrefix in response.CommonPrefixes)
                {
                    var trimmed = commonPrefix.TrimEnd('/').Substring(prefix.Length);
                    if (long.TryParse(trimmed, out var account))
                        accounts.Add(account);
                }

                continuationToken = response.IsTruncated ? response.NextContinuationToken : null;
            }
            while (continuationToken != null);

            return accounts.OrderBy(a => a).ToArray();
        }

        public async Task RemoveOldStagingAsync(CancellationToken cancel)
        {
            // Algorithm overview:
            // 1) Enumerate objects under the staging prefix only.
            // 2) Parse the leading yyyy-MM-dd segment from each staging key.
            // 3) Keep objects older than 2 days.
            // 4) Delete selected staging objects in batches of up to 1000 keys.
            var stagingPrefix = NormalizePrefix(_stagingPrefix);
            var stagingCutoffDate = DateTime.UtcNow.Date.AddDays(-2);
            string continuationToken = null;
            var stagingKeysToDelete = new List<KeyVersion>();

            do
            {
                var list = await Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = stagingPrefix,
                    ContinuationToken = continuationToken
                }, cancel).ConfigureAwait(false);

                foreach (var stagingObject in list.S3Objects)
                {
                    if (!TryGetStagingObjectDate(stagingObject.Key, stagingPrefix, out var stagingDate))
                        continue;

                    if (stagingDate >= stagingCutoffDate)
                        continue;

                    stagingKeysToDelete.Add(new KeyVersion { Key = stagingObject.Key });
                    if (stagingKeysToDelete.Count == 1000)
                    {
                        await DeleteStagingBatchAsync(stagingKeysToDelete, cancel).ConfigureAwait(false);
                        stagingKeysToDelete.Clear();
                    }
                }

                continuationToken = list.IsTruncated ? list.NextContinuationToken : null;
            }
            while (continuationToken != null);

            if (stagingKeysToDelete.Count > 0)
            {
                await DeleteStagingBatchAsync(stagingKeysToDelete, cancel).ConfigureAwait(false);
            }
            
            async Task DeleteStagingBatchAsync(List<KeyVersion> keysToDelete, CancellationToken ct)
            {
                await Client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = _bucket,
                    Objects = keysToDelete
                }, ct).ConfigureAwait(false);
            }

            static bool TryGetStagingObjectDate(string objectKey, string prefix, out DateTime stagingDate)
            {
                stagingDate = default;

                if (string.IsNullOrWhiteSpace(objectKey))
                    return false;

                if (!objectKey.StartsWith(prefix, StringComparison.Ordinal))
                    return false;

                var relativeKey = objectKey.Substring(prefix.Length);
                var firstSlash = relativeKey.IndexOf('/');
                if (firstSlash <= 0)
                    return false;

                var dateSegment = relativeKey.Substring(0, firstSlash);
                return DateTime.TryParseExact(
                    dateSegment,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out stagingDate);
            }
        }

        private static async Task EnsureBucketExists(IAmazonS3 client, string bucket)
        {
            if (await AmazonS3Util.DoesS3BucketExistV2Async(client, bucket).ConfigureAwait(false))
                return;

            await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket }).ConfigureAwait(false);
        }

        private async Task DeletePrefix(string prefix)
        {
            var normalized = NormalizePrefix(prefix);
            string continuationToken = null;

            do
            {
                var list = await Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = normalized,
                    ContinuationToken = continuationToken
                }).ConfigureAwait(false);

                if (list.S3Objects.Count > 0)
                {
                    var delete = new DeleteObjectsRequest
                    {
                        BucketName = _bucket,
                        Objects = list.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                    };

                    await Client.DeleteObjectsAsync(delete).ConfigureAwait(false);
                }

                continuationToken = list.IsTruncated ? list.NextContinuationToken : null;
            }
            while (continuationToken != null);
        }

        private static string NormalizePrefix(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
            return prefix.EndsWith("/") ? prefix : prefix + "/";
        }

        private static S3ConnectionSettings ParseSettings(string config)
        {
            if (string.IsNullOrWhiteSpace(config))
                throw new ArgumentException("S3 config is empty.");

            var settings = new S3ConnectionSettings();

            foreach (var part in config.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;

                var key = kv[0].Trim();
                var value = kv[1].Trim();

                switch (key.ToLowerInvariant())
                {
                    case "accesskey":
                        settings.AccessKey = value;
                        break;
                    case "secretkey":
                        settings.SecretKey = value;
                        break;
                    case "sessiontoken":
                        settings.SessionToken = value;
                        break;
                    case "serviceurl":
                        settings.ServiceUrl = value;
                        break;
                    case "region":
                        settings.Region = value;
                        break;
                    case "bucket":
                        settings.Bucket = value;
                        break;
                    case "forcepathstyle":
                        settings.ForcePathStyle = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                }
            }

            return settings;
        }

        private static IAmazonS3 BuildClient(S3ConnectionSettings settings)
        {
            var config = new AmazonS3Config();

            if (!string.IsNullOrWhiteSpace(settings.ServiceUrl))
                config.ServiceURL = settings.ServiceUrl;

            if (!string.IsNullOrWhiteSpace(settings.Region))
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(settings.Region);

            if (settings.ForcePathStyle)
                config.ForcePathStyle = true;

            if (!string.IsNullOrWhiteSpace(settings.AccessKey) &&
                !string.IsNullOrWhiteSpace(settings.SecretKey))
            {
                AWSCredentials creds;
                if (string.IsNullOrWhiteSpace(settings.SessionToken))
                    creds = new BasicAWSCredentials(settings.AccessKey, settings.SecretKey);
                else
                    creds = new SessionAWSCredentials(settings.AccessKey, settings.SecretKey, settings.SessionToken);

                return new AmazonS3Client(creds, config);
            }

            return new AmazonS3Client(config);
        }
    }

    public sealed class S3ConnectionSettings
    {
        public string AccessKey { get; set; }
        public string SecretKey { get; set; }
        public string SessionToken { get; set; }
        public string ServiceUrl { get; set; }
        public string Region { get; set; }
        public string Bucket { get; set; }
        public bool ForcePathStyle { get; set; }
    }
}
