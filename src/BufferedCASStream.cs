using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr;

/// <summary> Like <see cref="CASStream"/> but buffers _all_ the write calls. </summary>
public sealed class BufferedCASStream : Stream
{
    /// <summary> The underlying writer. </summary>
    private readonly StoreWriter _w;

    /// <summary> If true, closing the stream commits the writer. </summary>
    private readonly bool _commit;

    public BufferedCASStream(StoreWriter w, bool commit = true)
    {
        _w = w;
        _commit = commit;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
        // A content-addressable file does not exist until it's committed, 
        // so flushing does not really have a meaning.
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new InvalidOperationException("BufferedCASStream is non-seekable.");
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        throw new InvalidOperationException("BufferedCASStream is non-seekable.");
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new InvalidOperationException("BufferedCASStream is non-readable.");
    }

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) =>
        _w.Write(buffer, offset, count);

    /// <inheritdoc/>
    public override void WriteByte(byte value) =>
        _w.WriteByte(value);

    /// <inheritdoc/>
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel)
    {
        // we do write "in sync", but we are bufferring here, as the store
        // writer is buffering.
        _w.Write(buffer, offset, count);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
    {
        // we do write "in sync", but we are bufferring here, as the store
        // writer is buffering.
        _w.Write(buffer.Span);
        return default; // ValueTask.CompledTask if not netstandard
    }

    /// <inheritdoc/>
    public override void Close()
    {
        if (_commit)                
            // Don't wait for commit to complete. Anyone holding the writer will
            // be able to view the "CommitWasRequested"
            _w.CommitAsync(CancellationToken.None);
    }
    
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _w.Size;

    public override long Position
    {
        get { return _w.Size; }
        set { throw new InvalidOperationException("BuffereCASStream is non-seekable."); }
    }
}
