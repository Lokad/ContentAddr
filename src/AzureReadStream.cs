using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Azure
{
    /// <summary> A stream that reads from an immutable Azure blob. </summary>
    /// <remarks>
    ///     This is a reimplementation of the blob read stream from the Azure Storage
    ///     package, taking into account the fact that the blob is immutable (and 
    ///     therefore, the ETag checks are not necessary).
    /// 
    ///     The stream is optimized for two modes: 
    /// 
    ///      - async mode, where the only called API is <see cref="ReadAsync"/> for
    ///        reading large sets of bytes. This mode is mostly when reading Ionic, 
    ///        or when parsing CSV files. It uses no internal buffering, and instead
    ///        reads directly from the blob to the byte array.
    /// 
    ///      - sync mode, where the called APIs are the synchronous functions 
    ///        <see cref="Read"/> and <see cref="Stream.ReadByte"/>. This mode 
    ///        assumes that many reads will be performed and uses a buffer of size
    ///        up to 4MB.
    /// </remarks>
    public sealed class AzureReadStream : Stream
    {
        /// <summary> Read data from this blob. </summary>
        private readonly BlobClient _blob;

        /// <summary> The current position within the blob. </summary>
        private long _position;

        /// <summary> The default size of the buffer. </summary>
        private const int BufferSize = 4 * 1024 * 1024;

        /// <summary> Used for buffering requests when running in "sync" mode. </summary>
        /// <remarks>
        ///     When not null, the buffer is filled from 0 to <see cref="_bufferEnd"/> exclusive.
        /// </remarks>
        private byte[] _buffer;

        /// <summary> 
        ///     The offset of the byte in <see cref="_buffer"/> that corresponds to <see cref="_position"/>. 
        /// </summary>
        private int _bufferOffset;

        /// <summary> The past-the-end position in the buffer. </summary>
        /// <remarks> 
        ///     Most of the time, this will be the length of the buffer. 
        /// 
        ///     If <see cref="_buffer"/> is null, will be equal to <see cref="_bufferOffset"/>.
        /// </remarks>
        private int _bufferEnd;

        public AzureReadStream(BlobClient blob, long size)
        {
            _blob = blob;
            Length = size;
        }

        /// <summary>
        ///     Produce an URL that can be used to download the blob (or part of the blob).
        /// </summary>
        private static Uri DownloadUrl(BlobClient blob) =>
            blob.CanGenerateSasUri
                ? blob.GenerateSasUri(new BlobSasBuilder(BlobContainerSasPermissions.Read,
                                        new DateTimeOffset(DateTime.UtcNow.AddDays(1))))
                // If the authentication model cannot support the generation of a SAS URI, we
                // expect it's because the original connection mode is already based on a SAS,
                // and so the blob's URI should already include the SAS.
                : blob.Uri;

        /// <see cref="Stream.ReadAsync(byte[],int,int,CancellationToken)"/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancel)
        {
            var position = _position;

            count = (int)Math.Min(count, Length - position);

            if (count > 0)
            {
                await AzureRetry.Do(
                    c => DownloadRangeAsync(_blob, buffer, offset, position, count, c),
                    cancel).ConfigureAwait(false);

                // Change the state of the stream only after the read has succeeded. This way,
                // if an exception is thrown, the stream remains in its previous state and so the
                // call can be retried by the caller.
                _position += count;
            }

            return count;
        }

        /// <summary>
        ///     Downloads a range of data from <paramref name="blob"/>, starting at position <paramref name="sourceOffset"/> and
        ///     length <paramref name="count"/> bytes. Data is written to buffer <paramref name="into"/> starting at 
        ///     position <paramref name="intoOffset"/>.
        /// </summary>
        public static async Task DownloadRangeAsync(BlobClient blob, byte[] into, int intoOffset, long sourceOffset, int count, CancellationToken cancel)
        {
            var result = await blob.DownloadStreamingAsync(
                range: new HttpRange(sourceOffset, count),
                conditions: null,
                rangeGetContentHash: false,
                cancellationToken: cancel);

            using var stream = result.Value.Content;

            while (count > 0)
            {
                var read = await stream.ReadAsync(into, intoOffset, count);
                if (read == 0) throw new InvalidOperationException("Unexpected end-of-stream on DownloadStreamingAsync");
                intoOffset += read;
                count -= read;
            }
        }

        /// <see cref="Stream.Flush"/>
        public override void Flush() =>
            throw new NotSupportedException();

        /// <see cref="Stream.Seek"/>
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
                    // Still within the range represented by the buffer.
                    _bufferOffset = (int)(offset - bufferStart);
                }
                else
                {
                    // Outside the range, so buffer becomes useless.
                    DropSyncBuffer();
                }
            }

            return _position = offset;
        }

        /// <see cref="Stream.SetLength"/>
        public override void SetLength(long value) =>
            throw new NotSupportedException();

        /// <see cref="Stream.Read"/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            var position = _position;

            count = (int)Math.Min(count, Length - position);

            if (count == 0) return 0;

            if (count >= BufferSize)
            {
                // Very large read does not follow the usual "sync" path.
                DropSyncBuffer();
                _position += count;
                AzureRetry.Do(
                    c => DownloadRangeAsync(_blob, buffer, offset, position, count, c),
                    CancellationToken.None).Wait();
                return count;
            }

            var subCount = _bufferEnd - _bufferOffset;
            if (subCount >= count)
            {
                // Read falls within the currently available buffer
                Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset, count);
                _bufferOffset += count;
                _position += count;
            }
            else
            {
                if (subCount > 0)
                {
                    // Read crosses two sync buffers.
                    Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset, subCount);
                    count -= subCount;
                    offset += subCount;
                    _position += subCount;
                }

                // Read needs bytes from a new buffer
                LoadSyncBuffer();

                Buffer.BlockCopy(_buffer, _bufferOffset, buffer, offset, count);
                _bufferOffset += count;
                _position += count;
            }

            return (int)(_position - position);
        }

        /// <summary> Drops the synchronous <see cref="_buffer"/>, if present. </summary>        
        private void DropSyncBuffer()
        {
            _buffer = null;
            _bufferOffset = _bufferEnd = 0;
        }

        /// <see cref="Stream.ReadByte"/>
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

        /// <summary> Download bytes into the sync buffer, filling it. </summary>
        /// <remarks> The reading starts at <see cref="_position"/>. </remarks>
        private void LoadSyncBuffer()
        {
            if (_buffer == null)
                _buffer = new byte[Math.Min(Length, BufferSize)];

            _bufferOffset = 0;
            _bufferEnd = (int)Math.Min(_buffer.Length, Length - _position);
            AzureRetry.Do(
                c => DownloadRangeAsync(_blob, _buffer, 0, _position, _bufferEnd, c),
                CancellationToken.None).Wait();
        }

        /// <see cref="Stream.Write"/>
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        /// <see cref="Stream.CanRead"/>
        public override bool CanRead => true;

        /// <see cref="Stream.CanSeek"/>
        public override bool CanSeek => true;

        /// <see cref="Stream.CanWrite"/>
        public override bool CanWrite => false;

        /// <see cref="Stream.Length"/>
        public override long Length { get; }

        /// <see cref="Stream.Position"/>
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
