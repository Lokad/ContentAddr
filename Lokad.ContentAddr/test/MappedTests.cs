using Lokad.ContentAddr.Mapped;
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Lokad.ContentAddr.Tests;

public class MappedTests : UploadFixture, IDisposable
{
    private readonly string _path;
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedStore _store;

    public MappedTests()
    {
        _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _mmf = MemoryMappedFile.CreateFromFile(_path, FileMode.Create, null, 10 << 20 /* 10 MB */);
        Store = _store = new MemoryMappedStore(0, _mmf, false);
    }

    public void Dispose()
    {
        _store.Dispose();
        File.Delete(_path);
    }

    [Fact]
    public async Task truncate()
    {
        WrittenBlob r1 = await Store.WriteAsync([0x01, 0x02], default);
        WrittenBlob r2 = await Store.WriteAsync([0x01, 0x02, 0x03], default);
        long size = _store.Size;
        WrittenBlob r3 = await Store.WriteAsync([0x01, 0x02, 0x03, 0x04], default);

        _store.Truncate(size);

        using (Stream s1 = await Store[r1.Hash].OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[6];
            Assert.Equal(2, await s1.ReadAsync(bytes, CancellationToken.None)); 
            Assert.Equal([0x01, 0x02, 0, 0, 0, 0], bytes);
        }

        await using (Stream s2 = await Store[r2.Hash].OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[6];
            Assert.Equal(3, await s2.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0x01, 0x02, 0x03, 0, 0, 0], bytes);
        }

        Assert.False(await Store[r3.Hash].ExistsAsync(default));
    }

    [Fact]
    public async Task share()
    {
        WrittenBlob r1 = await Store.WriteAsync([0x01, 0x02], default);
        WrittenBlob r2 = await Store.WriteAsync([0x01, 0x02, 0x03], default);

        using MemoryMappedStore store2 = new MemoryMappedStore(0, _mmf, true);

        WrittenBlob r3 = await store2.WriteAsync([0x01, 0x02, 0x03, 0x04], default);

        await using (Stream s1 = await store2[r1.Hash].OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[6];
            Assert.Equal(2, await s1.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0x01, 0x02, 0, 0, 0, 0], bytes);
        }

        await using (Stream s2 = await store2[r2.Hash].OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[6];
            Assert.Equal(3, await s2.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0x01, 0x02, 0x03, 0, 0, 0], bytes);
        }

        await using (Stream s3 = await Store[r3.Hash].OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[6];
            Assert.Equal(4, await s3.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0x01, 0x02, 0x03, 0x04, 0, 0], bytes);
        }
    }
}
