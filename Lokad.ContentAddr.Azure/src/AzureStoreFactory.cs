using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> Generates <see cref="AzureStore"/> instances for specific accounts. </summary>
    public sealed class AzureStoreFactory : IAzureStoreFactory
    {
        /// <summary> Staging blob container. </summary>
        private readonly BlobContainerClient _staging;

        /// <summary> Persistent blob container. </summary>
        private readonly BlobContainerClient _persist;

        /// <summary> Archive blob container. </summary>
        private readonly BlobContainerClient _archive;

        /// <summary> Deleted blob container. </summary>
        private readonly BlobContainerClient _deleted;

        /// <summary> Container prefix, if in testing. </summary>
        private readonly string _testPrefix;

        /// <summary> Called when committing blobs. </summary>
        public AzureWriter.OnCommit OnCommit { get; set; }

        /// <summary> The blob client
        public BlobServiceClient BlobClient { get; }

        public static IAzureStoreFactory ParseConfig(string config, bool readOnly = false, string testPrefix = null)
        {
            string[] configs = SplitDualConfig(config);
            if (configs.Length == 1)
                return new AzureStoreFactory(config, readOnly, testPrefix);
            else return new DualAzureStoreFactory(configs[0], configs[1], readOnly, testPrefix);
        }

        public AzureStoreFactory(string config, bool readOnly = false, string testPrefix = null) :
            this(new BlobServiceClient(config), readOnly, testPrefix)
        { }

        public AzureStoreFactory(BlobServiceClient client, bool readOnly = false, string testPrefix = null)
        {
            var persistName = testPrefix == null ? "persist" : testPrefix + "-persist";
            var stagingName = testPrefix == null ? "staging" : testPrefix + "-staging";
            var archiveName = testPrefix == null ? "archive" : testPrefix + "-archive";
            var deletedName = testPrefix == null ? "deleted" : testPrefix + "-deleted";

            _testPrefix = testPrefix;
            BlobClient = client;
            _persist = client.GetBlobContainerClient(persistName);

            if (readOnly)
            {
                if (!_persist.ExistsAsync().Result)
                    throw new Exception("Cannot create persistent container in read-only mode.");
            }
            else
            {
                if (!_persist.Exists())
                    _persist.CreateIfNotExistsAsync().Wait();

                _staging = client.GetBlobContainerClient(stagingName);
                if (!_staging.Exists())
                    _staging.CreateIfNotExistsAsync().Wait();

                _archive = client.GetBlobContainerClient(archiveName);
                if (!_archive.Exists())
                    _archive.CreateIfNotExistsAsync().Wait();

                _deleted = client.GetBlobContainerClient(deletedName);
                if (!_deleted.Exists())
                    _deleted.CreateIfNotExistsAsync().Wait();
            }
        }

        /// <summary> A read-write store for the specified account. </summary>
        public IAzureStore ForAccount(long account)
        {
            if (_staging == null)
                throw new InvalidOperationException("Cannot use 'ForAccount' in read-only mode.");

            return new AzureStore(account.ToString(CultureInfo.InvariantCulture), _persist, _staging, _archive, _deleted, OnCommit);
        }

        /// <see cref="IStoreFactory.this"/>
        public IStore<IReadBlobRef> this[long account] => ForAccount(account);

        /// <see cref="IStoreFactory.ReadOnlyStore"/>
        public IReadOnlyStore<IReadBlobRef> ReadOnlyStore(long account) =>
            ReadOnlyForAccount(account);

        /// <summary> A read-only store for the specified account. </summary>
        public IAzureReadOnlyStore ReadOnlyForAccount(long account) =>
            new AzureReadOnlyStore(account.ToString(CultureInfo.InvariantCulture), _persist, _deleted);

        /// <summary> Deletes all contents. Only available when testing. </summary>
        public void Delete()
        {
            if (_testPrefix == null)
                throw new InvalidOperationException("Cannot delete non-test persistent store.");

            _persist.DeleteIfExistsAsync().Wait();
            _archive.DeleteIfExistsAsync().Wait();
            _staging.DeleteIfExistsAsync().Wait();
            _deleted.DeleteIfExistsAsync().Wait();
        }

        /// <see cref="IStoreFactory.Describe"/>
        public string Describe() => "[CAS] " + _persist.Uri;

        /// <summary> Retrieve all accounts that have blobs in stores from this factory. </summary>
        /// <remarks> Accounts are sorted in ascending order. </remarks>
        public async Task<IReadOnlyList<long>> GetAccountsAsync(CancellationToken cancel)
        {
            var accounts = new List<long>();


            var result = await _persist.GetBlobsByHierarchyAsync(
                traits: BlobTraits.None,
                states: BlobStates.None,
                prefix: "",
                delimiter: "/",
                cancellationToken: cancel).ToListAsync().ConfigureAwait(false);

            foreach (var item in result)
            {
                if (item.IsPrefix && long.TryParse(item.Prefix.Trim('/'), out var account))
                    accounts.Add(account);
            }


            accounts.Sort();

            return accounts;
        }

        /// <summary> Splits a string of a dual Azure storage config into two strings.
        /// The dual string should be composed of two Azure storage config separated by "||"
        /// </summary>
        public static string[] SplitDualConfig(string config)
        {
            return config.Split(new[] { "||" }, StringSplitOptions.None);
        }
    }
}

