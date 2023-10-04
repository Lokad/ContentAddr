using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace Lokad.ContentAddr.Mapped
{
    /// <summary> A stream that reads from a `MemoryMappedViewAccessor`. </summary>
    internal sealed class ReadMemoryStream : Stream
    {
        private readonly MemoryMappedViewAccessor _mmva;

        private readonly long _offset;
        
        private long _position;

        public ReadMemoryStream(MemoryMappedViewAccessor buffer, long offset, long count)
        {
            _mmva = buffer;
            _offset = offset;
            Length = count;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length { get; }

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0 || value >= Length)
                    throw new ArgumentOutOfRangeException(nameof(value));
                _position = (int)value;
            }
        }

        public override void Flush() => 
            throw new NotSupportedException();
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, Length - Position);

            var realRead = _mmva.ReadArray(_offset + _position, buffer, offset, read);

            if (realRead != read)
                throw new InvalidOperationException(
                    $"Expected to read {read} bytes but only found {realRead}");

            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin: return Position = offset;
                case SeekOrigin.End: return Position = Length + offset;
                case SeekOrigin.Current: return Position += offset;
                default: throw new ArgumentOutOfRangeException(nameof(origin));
            }
        }

        public override void SetLength(long value) => 
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => 
            throw new NotSupportedException();
    }
}
