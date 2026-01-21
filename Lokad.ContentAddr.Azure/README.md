Azure Blob Storage support for [Lokad.ContentAddr](https://github.com/Lokad/ContentAddr). 

The relevant types are `AzureStoreFactory` (for a multi-tenant store) and `AzureStore` (for
a single-tenant store). These implement the `IStoreFactory` and `IStore` interfaces from
the base Lokad.ContentAddr library. 

## Data Layout 

This creates three containers in the Azure Blob Storage account: 

 - `persist` will contain the persistent blobs, named `<realm>/<hash>` (for instance, a
    blob `AC03061D6376491889AE7B1D6661AC94` in realm `85055` would be named 
    `85055/AC03061D6376491889AE7B1D6661AC94`).

 - `staging` will contain temporary blobs as they are being uploaded to
   the content-addressable store. This container can be safely emptied without
   losing any committed data.

 - `archive` follows the same naming scheme as the persistent blobs, and will 
    contain any persistent blobs that are moved to the archived storage tier 
    in order to reduce costs. It is expected that blobs in this container
    contain the gzipped version of the persistent blobs, to further reduce
    the size and therefore the costs. 

This library supports two upload models for uploading blobs:

### Uploading with the Lokad.ContentAddr library

This uses the `IStore` interface to push data to Azure Blob Storage.
For example:

```c#
AzureStore store = ...;
store.WriteAsync(new byte[] {...}, cancel);
```

More information about the implementation details (not relevant to normal use
of the library) follows:

Data is accumulated in-memory up to a certain point (around 4MB), after which 
a block blob is created in the `staging` container, and the data is written
as separate blocks to that blob, to avoid consuming too much memory. 

The hash of the blob is calculated on-the-fly.

Once the entire data has been provided, the hash becomes known. The library then
determines a blob with the same hash already exists for the tenant. If it does,
the temporary block blob is deleted. If it does not, the temporary block blob is
committed, then copied to the `persist` container with the appropriate hash.

### Uploading from a different source

It is also possible to give a different client an upload link with a shared access
signature (for instance, an in-browser JavaScript uploader). This is done
using the appropriate method on the store: 

```c#
AzureStore store = ...;
string identifier = Guid.NewGuid().ToString();
TimeSpan life = TimeSpan.FromMinutes(10);
Url url = store.GetSignedUploadUrl(identifier, life);
```

This will create a Shared Access Signature that lasts 10 minutes, that allows the
client to write to that specific blob in the `staging` container. 

Once the client has finished uploading, it should contact the issuer of the signed
URL in order to commit the upload to the store. The issuer then invokes: 

```c#
// Same store and identifier as were used to generate the URL
AzureStore store = ...;
string identifier = ...; 
await store.CommitTemporaryBlob(identifier, cancellationToken);
```

This causes the store to download the entire uploaded blob, compute its hash,
and then move it to the `persist` container (if a copy is not already present 
there). 

## Download links

It is possible to produce a short-lived URL that allows anyone to download a 
blob from the `persist` container.

```c#
    AzureStore store;
    IAzureBlobRef blob = store[new Hash("AC03061D6376491889AE7B1D6661AC94")];
    Url download = await blob.GetDownloadUrlAsync(
        now: DateTime.UtcNow,
        life: TimeSpan.FromMinutes(10),
        filename: "budget.xlsx",
        contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        cancel: cancellationToken);
```

The URL contains a `Content-Disposition: attachment` header that causes browsers 
to interpret it as a downloadable file (with the file name specified through the
`filename` argument of the function). 

