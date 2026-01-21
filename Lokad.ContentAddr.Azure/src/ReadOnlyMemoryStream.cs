using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary>
    ///     Use the <see cref="ReadOnlyMemoryStream.Create"/> method to turn 
    ///     a <see cref="ReadOnlyMemory{byte}"/> into a <see cref="Stream"/>.
    /// </summary>
    internal class ReadOnlyMemoryStream : Stream
    {
        /// <summary> Underlying data that will be read by the stream.</summary>
        private readonly ReadOnlyMemory<byte> _bytes;

        /// <summary> Position of the read cursor. </summary>
        private int _position;

        private ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory)
        {
            _bytes = memory;
        }

        public override long Position 
        {
            get => _position;
            set
            {
                if (value > _bytes.Length || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _position = (int)value;
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _bytes.Length;

        public static Stream Create(ReadOnlyMemory<byte> memory)
        {
            if (memory.Length == 0) 
                return new MemoryStream();

            if (MemoryMarshal.TryGetArray(memory, out var segment))
                return new MemoryStream(segment.Array, segment.Offset, segment.Count);

            return new ReadOnlyMemoryStream(memory);
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var realCount = Math.Min(count, _bytes.Length - _position);
            _bytes.Span.Slice(_position, realCount).CopyTo(buffer.AsSpan(offset, realCount));
            _position += realCount;
            return realCount;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
            Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var realCount = Math.Min(buffer.Length, _bytes.Length - _position);
            _bytes.Span.Slice(_position, realCount).CopyTo(buffer.Slice(0, realCount).Span);
            _position += realCount;
            return new ValueTask<int>(realCount);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.End => Length - offset,
                SeekOrigin.Current => Position + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
