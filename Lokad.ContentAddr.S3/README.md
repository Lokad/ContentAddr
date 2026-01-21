S3-compatible object storage support for [Lokad.ContentAddr](https://github.com/Lokad/ContentAddr).

The relevant types are `S3StoreFactory` (for a multi-tenant store) and `S3Store` (for
a single-tenant store). These implement the `IStoreFactory` and `IStore` interfaces from
the base Lokad.ContentAddr library.

## Data Layout

This uses a single bucket and three root prefixes (folders):

- `persist/` will contain the persistent blobs, named `<realm>/<hash>` (for instance, a
  blob `AC03061D6376491889AE7B1D6661AC94` in realm `85055` would be named
  `persist/85055/AC03061D6376491889AE7B1D6661AC94`).

- `staging/` will contain temporary blobs as they are being uploaded to
  the content-addressable store. This prefix can be safely emptied without
  losing any committed data.

- `deleted/` will contain JSON metadata describing deletions.

## Configuration

The S3 configuration string is a semi-colon separated list of key/value pairs:

```
Bucket=my-bucket;Region=us-east-1;AccessKey=...;SecretKey=...;ServiceURL=https://s3.example.com;ForcePathStyle=true
```

Only `Bucket` is required; credentials and endpoints follow the standard AWS SDK
defaults if not specified.

## Uploading with the Lokad.ContentAddr library

This uses the `IStore` interface to push data to S3-compatible storage.
For example:

```c#
S3Store store = ...;
store.WriteAsync(new byte[] {...}, cancel);
```

Data is accumulated in-memory up to a certain point (around 5MB), after which
a multipart upload is created in the `staging` prefix, and data is written
as separate parts to that object.

The hash of the blob is calculated on-the-fly. Once the entire data has been
provided, the hash becomes known. The library then determines whether a blob
with the same hash already exists for the tenant. If it does, the temporary
object is deleted. If it does not, the temporary object is copied to
`persist/<realm>/<hash>`.

## Download links

It is possible to produce a short-lived URL that allows anyone to download a
blob from the `persist` prefix.

```c#
S3Store store;
IS3ReadBlobRef blob = store[new Hash("AC03061D6376491889AE7B1D6661AC94")];
Uri download = await blob.GetDownloadUrlAsync(
    now: DateTime.UtcNow,
    life: TimeSpan.FromMinutes(10),
    filename: "budget.xlsx",
    contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    cancel: cancellationToken);
```

The URL contains a `Content-Disposition: attachment` header that causes browsers
to interpret it as a downloadable file (with the file name specified through the
`filename` argument of the function).
