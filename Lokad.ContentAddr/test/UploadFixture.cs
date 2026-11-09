using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Lokad.ContentAddr.Tests;

public abstract class UploadFixture
{
    protected IStore<IReadBlobRef> Store { get; set; }

    [Fact]
    public async Task empty()
    {
        WrittenBlob r = await Store.WriteAsync([], CancellationToken.None);
        Assert.Equal("D41D8CD98F00B204E9800998ECF8427E", r.Hash.ToString());
        Assert.Equal(0, r.Size);

        IReadBlobRef a = Store[ new Hash("D41D8CD98F00B204E9800998ECF8427E")];

        Assert.True(await a.ExistsAsync(CancellationToken.None));
        Assert.Equal(0, await a.GetSizeAsync(CancellationToken.None));

        using (System.IO.Stream s = await a.OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[10];
            Assert.Equal(0, await s.ReadAsync(bytes, CancellationToken.None));
        }
    }

    [Fact]
    public async Task multiple()
    {
        WrittenBlob r1 = await Store.WriteAsync([], default);
        Assert.Equal("D41D8CD98F00B204E9800998ECF8427E", r1.Hash.ToString());

        WrittenBlob r2 = await Store.WriteAsync([0x01, 0x02], default);
        Assert.Equal("0CB988D042A7F28DD5FE2B55B3F5AC7A", r2.Hash.ToString());

        WrittenBlob r3 = await Store.WriteAsync([0x01, 0x02, 0x03, 0x04], default);
        Assert.Equal("08D6C05A21512A79A1DFEB9D2A8F262F", r3.Hash.ToString());

        using (System.IO.Stream s1 = await Store[r1.Hash].OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[6];
            Assert.Equal(0, await s1.ReadAsync(bytes, CancellationToken.None));
        }

        using (System.IO.Stream s2 = await Store[r2.Hash].OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[6];
            Assert.Equal(2, await s2.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0x01, 0x02, 0, 0, 0, 0], bytes);
        }

        using (System.IO.Stream s3 = await Store[r3.Hash].OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[6];
            Assert.Equal(4, await s3.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0x01, 0x02, 0x03, 0x04, 0, 0], bytes);
        }
    }

    [Fact]
    public async Task small_file()
    {
        byte[] file = FakeFile(1024);
        Hash hash = Md5(file);

        Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", hash.ToString());

        WrittenBlob r = await Store.WriteAsync(file, CancellationToken.None);
        Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
        Assert.Equal(1024, r.Size);

        IReadBlobRef a = Store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

        Assert.True(await a.ExistsAsync(CancellationToken.None));
        Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

        using (System.IO.Stream s = await a.OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[10];
            Assert.Equal(10, await s.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0,1,2,3,4,5,6,7,8,9], bytes);
        }
    }

    [Fact]
    public async Task small_file_sub()
    {
        byte[] file = FakeFile(1024 * 2);

        WrittenBlob r = await Store.WriteAsync(file, 256, 1024, CancellationToken.None);
        Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
        Assert.Equal(1024, r.Size);

        IReadBlobRef a = Store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

        Assert.True(await a.ExistsAsync(CancellationToken.None));
        Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

        using (System.IO.Stream s = await a.OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[10];
            Assert.Equal(10, await s.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], bytes);
        }
    }
    
    [Fact]
    public async Task small_file_split()
    {
        byte[] file = FakeFile(1024);
        
        WrittenBlob r;
        using (StoreWriter w = Store.StartWriting())
        {
            for (int i = 0; i < file.Length; i += 256)
                await w.WriteAsync(file, i, 256, CancellationToken.None);

            r = await w.CommitAsync(CancellationToken.None);
        }

        Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
        Assert.Equal(1024, r.Size);

        IReadBlobRef a = Store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

        Assert.True(await a.ExistsAsync(CancellationToken.None));
        Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

        using (System.IO.Stream s = await a.OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[10];
            Assert.Equal(10, await s.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], bytes);
        }
    }

    [Fact]
    public async Task end_of_stream()
    {
        byte[] file = FakeFile(1024);

        WrittenBlob r;
        using (StoreWriter w = Store.StartWriting())
        {
            int pos = 0;
            await w.WriteAsync((bytes, offset, count, cancel) =>
            {
                if (pos == file.Length)
                {
                    pos++;
                    return Task.FromResult(0);
                }

                if (pos > file.Length)
                    Assert.Fail("Read function called after it had returned zero.");

                int read = Math.Min(Math.Min(file.Length - pos, 17), count);

                for (int i = 0; i < read; ++i)
                    bytes[offset + i] = file[pos + i];

                pos += read;

                return Task.FromResult(read);

            }, null, CancellationToken.None);

            r = await w.CommitAsync(CancellationToken.None);
        }

        Assert.Equal("B2EA9F7FCEA831A4A63B213F41A8855B", r.Hash.ToString());
        Assert.Equal(1024, r.Size);

        IReadBlobRef a = Store[new Hash("B2EA9F7FCEA831A4A63B213F41A8855B")];

        Assert.True(await a.ExistsAsync(CancellationToken.None));
        Assert.Equal(1024, await a.GetSizeAsync(CancellationToken.None));

        using (System.IO.Stream s = await a.OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[10];
            Assert.Equal(10, await s.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], bytes);
        }
    }

    [Fact]
    public async Task large_file()
    {
        // Exceeds the 4MB block size
        byte[] file = FakeFile(1024 * 1024 * 9);
        Hash hash = Md5(file);

        Assert.Equal("2A43A3F3D5B1A13F0B4C3369040D0919", hash.ToString());

        WrittenBlob r = await Store.WriteAsync(file, CancellationToken.None);
        Assert.Equal("2A43A3F3D5B1A13F0B4C3369040D0919", r.Hash.ToString());
        Assert.Equal(1024 * 1024 * 9, r.Size);

        IReadBlobRef a = Store[new Hash("2A43A3F3D5B1A13F0B4C3369040D0919")];

        Assert.True(await a.ExistsAsync(CancellationToken.None));
        Assert.Equal(1024 * 1024 * 9, await a.GetSizeAsync(CancellationToken.None));

        using (System.IO.Stream s = await a.OpenAsync(CancellationToken.None))
        {
            byte[] bytes = new byte[10];
            Assert.Equal(10, await s.ReadAsync(bytes, CancellationToken.None));
            Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], bytes);
        }

        using (System.IO.Stream s = await a.OpenAsync(CancellationToken.None))
            Assert.Equal(r.Hash, new Hash(MD5.Create().ComputeHash(s)));
    }
    
    private static Hash H(int i) => new Hash((ulong)i + 1, (ulong)i * 7919 + 3);

    private async Task<Hash> WriteWithRefs(byte[] data, params Hash[] refs)
    {
        using var w = Store.StartWriting();
        foreach (var r in refs) w.AddReference(r);
        return (await w.WriteAndCommitAsync(data, 0, data.Length, CancellationToken.None)).Hash;
    }

    [Fact]
    public async Task references_none()
    {
        var h = (await Store.WriteAsync([1, 2, 3], CancellationToken.None)).Hash;
        Assert.Empty(await Store[h].GetReferencesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task references_missing_blob()
    {
        await Assert.ThrowsAsync<NoSuchBlobException>(() =>
            Store[H(99)].GetReferencesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task references_distinct_ordered()
    {
        using var w = Store.StartWriting();
        Assert.True(w.AddReference(H(2)));
        Assert.True(w.AddReference(H(1)));
        Assert.False(w.AddReference(H(2)));
        w.Write([5, 6, 7], 0, 3);
        var blob = await w.CommitAsync(CancellationToken.None);

        Assert.Equal(new[] { H(2), H(1) }, await Store[blob.Hash].GetReferencesAsync(CancellationToken.None));
        Assert.Throws<InvalidOperationException>(() => w.AddReference(H(3)));
    }

    [Fact]
    public async Task references_many()
    {
        var refs = Enumerable.Range(0, 5000).Select(H).ToArray();
        var h = await WriteWithRefs([9, 9], refs);
        Assert.Equal(refs, await Store[h].GetReferencesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task references_existing_wins()
    {
        var h = await WriteWithRefs([4, 2], H(1));
        await WriteWithRefs([4, 2], H(2), H(3));
        Assert.Equal(new[] { H(1) }, await Store[h].GetReferencesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task references_filled_if_existing_empty()
    {
        var h = await WriteWithRefs([8, 8]);
        Assert.Empty(await Store[h].GetReferencesAsync(CancellationToken.None));

        await WriteWithRefs([8, 8], H(4), H(5));
        Assert.Equal(new[] { H(4), H(5) }, await Store[h].GetReferencesAsync(CancellationToken.None));

        await WriteWithRefs([8, 8], H(6));
        Assert.Equal(new[] { H(4), H(5) }, await Store[h].GetReferencesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task references_survive_import()
    {
        var h = await WriteWithRefs([3, 1, 4], H(7));
        var dst = new Lokad.ContentAddr.Memory.MemoryStore();
        await dst.ImportBlobs((IReadOnlyStore)Store, new[] { h }, null, CancellationToken.None);
        Assert.Equal(new[] { H(7) }, await dst[h].GetReferencesAsync(CancellationToken.None));
    }

    /// <summary> Misleading name. It does *not* convert a long to a byte[] </summary>
    protected static byte[] FakeFile(long n)
    {
        byte[] b = new byte[n];
        for (int i = 0; i < n; ++i) b[i] = (byte)i;
        return b;
    }

    protected static Hash Md5(byte[] input) => new Hash(MD5.Create().ComputeHash(input));
}
