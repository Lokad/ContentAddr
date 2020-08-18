A .NET library for a [Content-Addressable Store](https://en.wikipedia.org/wiki/Content-addressable_storage) : 

 - writing a binary blob to the store returns its MD5 hash, and
 - querying the store with a MD5 hash returns the original blob.

Two major benefits of content-addressable storage is automatic de-duplication (since the MD5 hash is computed, it can be used to detect whether the same value is already stored, either at write time or during a subsequent merge operation between stores) and automatic unique identifier generation in a distributed system.

The hash function is not cryptographically secure, therefore Lokad.ContentAddr will **not** prevent malicious users from generating collisions. However, the library provides a notion of **independent accounts** which do not share any data. It is recommended to place each user in a separate realm. 

This library provides an in-memory implementation and an on-disk implementation, as well as interfaces that can be used by other libraries to implement other back-ends (such as using Azure Blob Storage).

### Creating a Store

A read-write store is represented by interface `IStore`. Its read-only equivalent `IReadOnlyStore`. The library provides two default implementations: 

**On-disk storage** uses one file per blob, stored in the specified directory. A blob with hash `00112233445566778899aabbccddeeff` will be stored at the relative path `./_00/112233445566778899aabbccddeeff`. 

```c#
using Lokad.ContentAddr;
using Lokad.ContentAddr.Disk;

IStore rwStore = new DiskStore("./example");
IReadOnlyStore roStore = new DiskReadOnlyStore("./example");
```

**In-memory storage** is mostly intended for testing:

```c#
using Lokad.ContentAddr;
using Lokad.ContentAddr.Memory;

IStore rwStore = new MemoryStore();
IReadOnlyStore roStore = rwStore; 
```

#### Multi-account stores

To support multi-user scenarios, each user should have its own separate store identified by its **account identifier**. Lokad.ContentAddr provides an `IStoreFactory` interface which allows creating a store from the account identifier:

```c#
using Lokad.ContentAddr;
using Lokad.ContentAddr.Disk;

long account = 1337L;

IStoreFactory factory = new DiskStoreFactory("/var/data/cas");

IStore rwStore = factory[account];
IReadOnlyStore roStore = factory.ReadOnlyStore(account);
```

In the above example, it would be possible to `IReadOnlyStore roStore = rwStore;` in theory. In practice, if a factory is configured to only allow read-only stores, `factory[account]` will throw (because its intent is to return a read-write store), so it is recommended to call `factory.ReadOnlyStore(account)` when the intent is to create a read-only store. 

### Writing to a store

#### High-level 

The high-level process to write to a store is to invoke one of the following extension methods: 

```c#
// Write the entire byte array
WrittenBlob a = await rwStore.WriteAsync(new byte[] {...}, cancel);

// Write 'count' bytes from the array, starting at 'offset'
WrittenBlob b = await rwStore.WriteAsync(new byte[] {...}, offset, count, cancel);

// Write from the stream, starting at the current position and up to the end
Stream stream = ...;
WrittenBlob c = await rwStore.WriteAsync(stream, cancel);

// Write from the stream, using the specified buffer size (in bytes)
WrittenBlob d = await rwStore.WriteAsync(stream, 4096, cancel);
```

The returned `WrittenBlob` contains two fields, `Hash` (the hash of the blob) and `Size` (the number of bytes in the blob).

#### Low-level

The low-level process to write to a store involves the following steps: 

```c#
// Create a StoreWriter, which represents a write transaction
using (StoreWriter w = rwStore.StartWriting())
{
    // Write to the writer: 
    await w.WriteAsync(bytes, offset, count, cancel);

    // Commit the write transaction: 
    WrittenBlob blob = await w.CommitAsync(cancel);
}
```

`WriteAsync` can be called as many times as necessary. Once `CommitAsync` is called, `WriteAsync` may no longer be called (but `CommitAsync` is idempotent and may be called several times). Also, while the `IStore` methods are re-entrant and can be used without locks, the `StoreWriter` methods are _not_ re-entrant.

If possible, it is recommended to merge the final `WriteAsync` call with the `CommitAsync`:

```
WrittenBlob blob = await w.WriteAndCommitAsync(bytes, offset, count, cancel);
```

This is equivalent to performing the two calls in succession, but lets the `StoreWriter` perform an important optimization: it can compute the full hash of the blob to detect whether the store already contains a copy of that blob, and thus avoid performing the write when that is the case.

#### Stream interoperability

To adapt to existing code which supports serializing data to a `Stream`, there exists a wrapper around the `StoreWriter`:

```c#
using (StoreWriter w = rwStore.StartWriting())
{
    using (Stream stream = new CASStream(w))
    {
        myExample.SerializeToStream(stream);
    }

    WrittenBlob blob = await w.CommitAsync(cancel);
}
```

### Reading from a store

Given an `IStore` (or `IReadOnlyStore`) and a `Hash`, it is possible to extract a **reference to a blob** (represented by class `IReadBlobRef`). 

```c#
Hash hash = ...; 

// Reference the blob. This never fails.
IReadBlobRef blobRef = roStore[hash];

// Check if the referenced blob exists in the store.
bool exists = await blobRef.ExistsAsync(cancel);

// The number of bytes in the blob, 
// throw NoSuchBlobException if blob does not exist
long size = await blobRef.GetSizeAsync(cancel);

// Open a seekable, read-only stream to read the blob contents,
// throw NoSuchBlobException if blob does not exist
using (Stream stream = await blob.OpenAsync(cancel))
{
    ...
}
```

Access to the store and to individual blobs is re-entrant.


