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
    /// <summary> Generates <see cref="DualAzureStore"/> instances for specific accounts. </summary>
    public sealed class DualAzureStoreFactory : IAzureStoreFactory
    {
        /// <summary> Staging blob container. </summary>
        private readonly BlobContainerClient _staging;

        /// <summary> Old persistent blob container. </summary>
        private readonly BlobContainerClient _oldPersist;

        /// <summary> New persistent blob container. </summary>
        private readonly BlobContainerClient _newPersist;

        /// <summary> Staging blob container. </summary>
        private readonly BlobContainerClient _archive;

        /// <summary> Deleted blob container. </summary>
        private readonly BlobContainerClient _deleted;

        /// <summary> Container prefix, if in testing. </summary>
        private readonly string _testPrefix;

        /// <summary> Called when committing blobs. </summary>
        public AzureWriter.OnCommit OnCommit { get; set; }

        /// <summary> The blob client of the new container
        public BlobServiceClient BlobClient { get; }

        public DualAzureStoreFactory(string oldConfig, string newConfig, bool readOnly = false, string testPrefix = null) :
            this(new BlobServiceClient(oldConfig), new BlobServiceClient(newConfig), readOnly, testPrefix)
        { }

        public DualAzureStoreFactory(BlobServiceClient oldClient, BlobServiceClient newClient, bool readOnly = false, string testPrefix = null)
        {
            var persistName = testPrefix == null ? "persist" : testPrefix + "-persist";
            var stagingName = testPrefix == null ? "staging" : testPrefix + "-staging";
            var archiveName = testPrefix == null ? "archive" : testPrefix + "-archive";
            var deletedName = testPrefix == null ? "deleted" : testPrefix + "-deleted";

            _testPrefix = testPrefix;
            BlobClient = newClient;
            _oldPersist = oldClient.GetBlobContainerClient(persistName);
            _newPersist = newClient.GetBlobContainerClient(persistName);
            _deleted = newClient.GetBlobContainerClient(deletedName);

            if (!readOnly)
            {
                if (!_newPersist.Exists())
                    _newPersist.CreateIfNotExistsAsync().Wait();
                _staging = newClient.GetBlobContainerClient(stagingName);
                if (!_staging.Exists())
                    _staging.CreateIfNotExistsAsync().Wait();
                _archive = newClient.GetBlobContainerClient(archiveName);
                if (!_archive.Exists())
                    _archive.CreateIfNotExistsAsync().Wait();
                _deleted = newClient.GetBlobContainerClient(deletedName);
                if (!_deleted.Exists())
                    _deleted.CreateIfNotExistsAsync().Wait();
            }
        }

        /// <summary> A read-write store for the specified account. </summary>
        public IAzureStore ForAccount(long account)
        {
            if (_staging == null)
                throw new InvalidOperationException("Cannot use 'ForAccount' in read-only mode.");

            return new DualAzureStore(account.ToString(CultureInfo.InvariantCulture), _oldPersist, _newPersist, _staging, _archive, _deleted, OnCommit);
        }

        /// <see cref="IStoreFactory.this"/>
        public IStore<IReadBlobRef> this[long account] => ForAccount(account);

        /// <see cref="IStoreFactory.ReadOnlyStore"/>
        public IReadOnlyStore<IReadBlobRef> ReadOnlyStore(long account) =>
            ReadOnlyForAccount(account);

        /// <summary> A read-only store for the specified account. </summary>
        public IAzureReadOnlyStore ReadOnlyForAccount(long account) =>
            new DualAzureReadOnlyStore(account.ToString(CultureInfo.InvariantCulture), _oldPersist, _newPersist, _deleted);

        /// <summary> Deletes all contents. Only available when testing. </summary>
        public void Delete()
        {
            if (_testPrefix == null)
                throw new InvalidOperationException("Cannot delete non-test persisent store.");

            _newPersist.DeleteIfExistsAsync().Wait();
            _oldPersist.DeleteIfExistsAsync().Wait();
            _staging.DeleteIfExistsAsync().Wait();
            _deleted.DeleteIfExistsAsync().Wait();
        }

        /// <see cref="IStoreFactory.Describe"/>
        public string Describe() => "[CAS] " + _newPersist.Uri;

        /// <summary> Retrieve all accounts that have blobs in stores from this factory. </summary>
        /// <remarks> Accounts are sorted in ascending order. </remarks>
        public async Task<IReadOnlyList<long>> GetAccountsAsync(CancellationToken cancel)
        {
            var oldAccounts = await GetAccountsAsync(_oldPersist, cancel);
            var newAccounts = await GetAccountsAsync(_newPersist, cancel);

            return oldAccounts.Concat(newAccounts).Distinct().OrderBy(acc => acc).ToArray();
        }

        /// <summary> Retrieve all accounts that have blobs in stores from this container. </summary>
        private async Task<List<long>> GetAccountsAsync(BlobContainerClient cbc, CancellationToken cancel)
        {
            var accounts = new List<long>();

            var result = await cbc.GetBlobsByHierarchyAsync(traits: BlobTraits.Metadata,
                states: BlobStates.None,
                prefix: "",
                cancellationToken: cancel).ToListAsync().ConfigureAwait(false);

            foreach (var item in result)
            {
                if (item.IsPrefix && long.TryParse(item.Prefix.Trim('/'), out var account))
                    accounts.Add(account);
            }

            accounts.Sort();

            return accounts;
        }
    }
}

