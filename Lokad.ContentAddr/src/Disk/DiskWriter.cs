using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Disk;

/// <summary>
///     Writes data to a temporary file, then pushes this file to 
///     the correct location.
/// </summary>
public sealed class DiskWriter : StoreWriter
{
    /// <summary> The directory to which to write the file. </summary>
    private readonly string _path;

    /// <summary> The path to the temporary file. </summary>
    private readonly string _tempPath;

    /// <summary> An open stream to a temporary file. </summary>
    /// <remarks> Created in the constructor, destroyed upon commit or destruction. </remarks>
    private readonly Stream _temp;

    public DiskWriter(string path)
    {
        _path = path;

        var tempDir = Path.Combine(_path, "tmp");
        Directory.CreateDirectory(tempDir);

        _tempPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"));
        _temp = File.Open(_tempPath, FileMode.CreateNew);
    }

    /// <inheritdoc cref="StoreWriter.DoWriteAsync"/>
    protected override Task DoWriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel) =>
        _temp.WriteAsync(buffer, cancel).AsTask();

    /// <inheritdoc cref="StoreWriter.DoCommitAsync"/>
    protected override Task DoCommitAsync(Hash hash, IReadOnlyList<Hash> references, CancellationToken cancel) =>
        DoOptCommitAsync(hash, references, null, cancel);

    /// <inheritdoc cref="StoreWriter.DoOptCommitAsync"/>
    protected override async Task DoOptCommitAsync(Hash hash, IReadOnlyList<Hash> references, Func<Task> optionalWrite, CancellationToken cancel)
    {
        var path = DiskStorePaths.PathOfHash(_path, hash);

        // The references are written before the blob becomes visible, but only if 
        // there are no existing references (existing references always win).
        if (references.Count > 0)
            WriteReferences(path, references);

        if (!File.Exists(path))
        {
            if (optionalWrite != null)
                await optionalWrite().ConfigureAwait(false);

            // Close the file before the move, but only after the optional 
            // writes are finished. 
            _temp.Close();

            // ReSharper disable AssignNullToNotNullAttribute
            // ... we know there's a dirname in the path (it's, at the very least, _path itself)
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            // ReSharper restore AssignNullToNotNullAttribute

            try
            {
                File.Move(_tempPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another writer raced us to create the destination file.
                // So, there's nothing left for us to do.
            }
        }
    }

    private void WriteReferences(string blobPath, IReadOnlyList<Hash> references)
    {
        var refsPath = DiskStorePaths.RefsPathOfBlobPath(blobPath);
        if (File.Exists(refsPath)) return;

        var bytes = new byte[references.Count * Hash.Size];
        for (var i = 0; i < references.Count; ++i)
            references[i].ToBytes(bytes.AsSpan(i * Hash.Size, Hash.Size));

        // ReSharper disable once AssignNullToNotNullAttribute
        Directory.CreateDirectory(Path.GetDirectoryName(blobPath));

        // Write to a temporary file and move, so that readers never see partial lists.
        var tempRefs = _tempPath + ".refs";
        File.WriteAllBytes(tempRefs, bytes);

        try
        {
            File.Move(tempRefs, refsPath);
        }
        catch (IOException) when (File.Exists(refsPath))
        {
            // Another writer raced us, and its references win.
            File.Delete(tempRefs);
        }
    }

    /// <summary> Closes the temporary file and deletes it, if it exists. </summary>
    public override void Dispose()
    {
        _temp.Dispose();
        if (File.Exists(_tempPath)) File.Delete(_tempPath);
        if (File.Exists(_tempPath + ".refs")) File.Delete(_tempPath + ".refs");
    }
}