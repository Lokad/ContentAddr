using Amazon.S3;
using Amazon.S3.Model;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.S3
{
    /// <summary> A stream that reads from an immutable S3 object. </summary>
    /// <remarks>
    ///     This is a reimplementation of a blob read stream, taking into account the
    ///     fact that the blob is immutable (and therefore, the ETag checks are not necessary).
    /// 
    ///     The stream is optimized for two modes:
    /// 
    ///      - async mode, where the only called API is <see cref="ReadAsync"/> for
    ///        reading large sets of bytes. This mode uses no internal buffering,
    ///        and instead reads directly from the object to the byte array.
    /// 
    ///      - sync mode, where the called APIs are the synchronous functions
    ///        <see cref="Read"/> and <see cref="Stream.ReadByte"/>. This mode
    ///        assumes that many reads will be performed and uses a buffer of size
    ///        up to 4MB.
    /// </remarks>
    public sealed class S3ReadStream : Stream
    {
        private readonly IAmazonS3 _client;
        private readonly string _bucket;
        private readonly string _key;
        private long _position;

        private const int BufferSize = 4 * 1024 * 1024;

        private byte[] _buffer;
        private int _bufferOffset;
        private int _bufferEnd;

        public S3ReadStream(IAmazonS3 client, string bucket, string key, long size)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _bucket = bucket ?? throw new ArgumentNullException(nameof(bucket));
            _key = key ?? throw new ArgumentNullException(nameof(key));
            Length = size;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel)
        {
            var position = _position;

            count = (int)Math.Min(count, Length - position);

            if (count > 0)
            {
                await S3Retry.Do(
                    c => DownloadRangeAsync(_client, _bucket, _key, buffer, offset, position, count, c),
                    cancel).ConfigureAwait(false);

                _position += count;
            }

            return count;
        }

        /// <summary>
        ///     Downloads a range of data from the object.
        /// </summary>
        public static async Task DownloadRangeAsync(
            IAmazonS3 client,
            string bucket,
            string key,
            byte[] into,
            int intoOffset,
            long sourceOffset,
            int count,
            CancellationToken cancel)
        {
            var end = sourceOffset + count - 1;
            var request = new GetObjectRequest
            {
                BucketName = bucket,
                Key = key,
                ByteRange = new ByteRange(sourceOffset, end)
            };

            using var response = await client.GetObjectAsync(request, cancel).ConfigureAwait(false);
            using var stream = response.ResponseStream;

            while (count > 0)
            {
                var read = await stream.ReadAsync(into, intoOffset, count, cancel).ConfigureAwait(false);
                if (read == 0) throw new InvalidOperationException("Unexpected end-of-stream on GetObjectAsync");
                intoOffset += read;
                count -= read;
            }
        }

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Current) offset += _position;
            if (origin == SeekOrigin.End) offset += Length;

            if (offset < 0 || offset > Length)
                throw new ArgumentOutOfRangeException(nameof(offset), $"Position {offset} should be in 0 .. {Length}");

            if (_buffer != null)
            {
                var bufferStart = _position - _bufferOffset;
                var bufferEnd = bufferStart + _bufferEnd;

                if (offset >= bufferStart && offset < bufferEnd)
                {
                    _bufferOffset = (int)(offset - bufferStart);
                }
                else
                {
                    DropSyncBuffer();
                }
            }

            return _position = offset;
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var position = _position;

            count = (int)Math.Min(count, Length - position);

            if (count == 0) return 0;

            if (count >= BufferSize)
            {
                DropSyncBuffer();
                _position += count;
                S3Retry.Do(
                    c => DownloadRangeAsync(_client, _bucket, _key, buffer, offset, position, count, c),
                    CancellationToken.None).Wait();
                return count;
            }

            var subCount = _bufferEnd - _bufferOffset;
            if (subCount >= count)
            {
                Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset, count);
                _bufferOffset += count;
                _position += count;
            }
            else
            {
                if (subCount > 0)
                {
                    Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset, subCount);
                    count -= subCount;
                    offset += subCount;
                    _position += subCount;
                }

                LoadSyncBuffer();

                Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset, count);
                _bufferOffset += count;
                _position += count;
            }

            return (int)(_position - position);
        }

        private void DropSyncBuffer()
        {
            _buffer = null;
            _bufferOffset = _bufferEnd = 0;
        }

        public override int ReadByte()
        {
            if (_bufferOffset == _bufferEnd)
            {
                if (_position == Length) return -1;
                LoadSyncBuffer();
            }

            _position++;
            return _buffer[_bufferOffset++];
        }

        private void LoadSyncBuffer()
        {
            if (_buffer == null)
                _buffer = new byte[(int)Math.Min(Length, BufferSize)];

            _bufferOffset = 0;
            _bufferEnd = (int)Math.Min(_buffer.Length, Length - _position);
            S3Retry.Do(
                c => DownloadRangeAsync(_client, _bucket, _key, _buffer, 0, _position, _bufferEnd, c),
                CancellationToken.None).Wait();
        }

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length { get; }

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Close()
        {
            DropSyncBuffer();
        }
    }
}
