using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr
{
    /// <summary>
    /// Like <see cref="CASStream"/> but buffers _all_ the write calls
    /// </summary>
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

        /// <see cref="Stream.Flush"/>
        public override void Flush()
        {
            // A content-addressable file does not exist until it's committed, 
            // so flushing does not really have a meaning.
        }

        /// <see cref="Stream.Seek"/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new InvalidOperationException("BufferedCASStream is non-seekable.");
        }

        /// <see cref="Stream.SetLength"/>
        public override void SetLength(long value)
        {
            throw new InvalidOperationException("BufferedCASStream is non-seekable.");
        }

        /// <see cref="Stream.Read"/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException("BufferedCASStream is non-readable.");
        }

        /// <see cref="Stream.Write"/>
        public override void Write(byte[] buffer, int offset, int count) =>
            _w.Write(buffer, offset, count);

        /// <see cref="Stream.WriteByte"/>
        public override void WriteByte(byte value) =>
            _w.WriteByte(value);

        /// <see cref="Stream.WriteAsync"/>
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel)
        {
            // we do write "in sync", but we are bufferring here, as the store
            // writer is buffering.
            _w.Write(buffer, offset, count);
            return Task.FromResult(0);
        }

        /// <see cref="Stream.Close"/>
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
}
