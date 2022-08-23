using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr
{
    public static class StoreExtensions
    {
        /// <summary> Write an entire buffer of bytes to the store. </summary>
        public static Task<WrittenBlob> WriteAsync(
            this IWriteOnlyStore store,
            byte[] buffer,
            CancellationToken cancel)
        =>
            store.WriteAsync(buffer.AsMemory(), cancel);

        /// <summary> Write a buffer of bytes to the store. </summary>
        public static Task<WrittenBlob> WriteAsync(
            this IWriteOnlyStore store,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancel)
        =>
            store.WriteAsync(buffer.AsMemory(offset, count), cancel);

        /// <summary> Write a buffer of bytes to the store. </summary>
        public static async Task<WrittenBlob> WriteAsync(
            this IWriteOnlyStore store,
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancel)
        {
            using (var writer = store.StartWriting())
                return await writer.WriteAndCommitAsync(buffer, cancel).ConfigureAwait(false);
        }

        /// <summary> Write a stream to a store. </summary>
        /// <remarks> Reads forward from the current position of the stream. </remarks>
        public static Task<WrittenBlob> WriteAsync(
            this IWriteOnlyStore store,
            Stream stream,
            CancellationToken cancel)
        =>
            // Default buffer size is 4MB, because Azure Storage likes it.
            store.WriteAsync(stream, 4 * 1024 * 1024, cancel);

        /// <summary> Write the contents of a stream to a writer. </summary>
        /// <param name="writer"> To be written to. </param>
        /// <param name="stream"> To be read from. </param>
        /// <param name="buffer"> Temporary buffer for copying data. </param>
        /// <param name="buffer2">
        ///     Optional secondary buffer. If null, the function may allocate a 
        ///     secondary buffer itself. In both cases, the secondary buffer will
        ///     be returned by the function (if null, and none was created, then
        ///     null will be returned).
        /// </param>
        /// <param name="thenCommit">
        ///     If true, commit the writer after writing the last buffer.
        /// </param>
        /// <param name="cancel"></param>
        private static async Task<byte[]> WriteAsync(
            StoreWriter writer,
            Stream stream,
            byte[] buffer, 
            byte[] buffer2, 
            bool thenCommit,
            CancellationToken cancel)
        {
            var bufferSize = buffer.Length;

            // The task that writes data in the background. Returns the buffer it
            // was using when it ends and doesn't need to buffer anymore, so that 
            // it can be reused in a double-buffering scheme.
            var writing = Task.FromResult(buffer2);

            while (true) // Read all pages and write them
            {
                var offset = 0; // Current offset within 'buffer'
                int read; // Bytes returned by last 'ReadAsync', zero if EoS

                do // Read a page of size 'bufferSize'
                {
                    offset += read = await stream
                        .ReadAsync(buffer, offset, bufferSize - offset, cancel)
                        .ConfigureAwait(false);

                } while (offset < bufferSize && read > 0);

                if (offset == 0) break;

                var isLast = read == 0;

                // Swap buffers and start writing.
                var next = await writing.ConfigureAwait(false);
                writing = InternalWrite(writer, buffer, offset, isLast && thenCommit, cancel);

                if (isLast) break;

                // Allocate a second buffer, but only if we need one
                buffer = next ?? (buffer2 = new byte[bufferSize]);

            } 

            await writing.ConfigureAwait(false);

            return buffer2;
        }

        /// <summary> Write a stream to a store. </summary>
        /// <remarks> Reads forward from the current position of the stream. </remarks>
        public static async Task<WrittenBlob> WriteAsync(
            this IWriteOnlyStore store,
            Stream stream,
            int bufferSize,
            CancellationToken cancel)
        {
            // Don't allocate memory we don't need.
            if (stream.CanSeek && stream.Length < bufferSize)
                bufferSize = (int) stream.Length;
            
            using (var writer = store.StartWriting())
            {
                var buffer = new byte[bufferSize]; 
                await WriteAsync(writer, stream, buffer, null, true, cancel).ConfigureAwait(false);
                return await writer.CommitAsync(cancel).ConfigureAwait(false);
            }
        }

        /// <see cref="WriteAsync(IWriteOnlyStore,Stream,int,CancellationToken)"/>
        private static async Task<byte[]> InternalWrite(StoreWriter writer, byte[] buffer, int count, bool thenCommit, CancellationToken cancel)
        {
            // This method returns the byte array to facilitate the control flow.
            // This way, the byte array is reserved until the write task completes.

            if (thenCommit)
                await writer.WriteAndCommitAsync(buffer, 0, count, cancel);
            else
                await writer.WriteAsync(buffer, 0, count, cancel);

            return buffer;
        }

        /// <summary>
        /// Copies blobs from <paramref name="source"/> store to <paramref name="destination"/> store.
        /// Also, this checks that the blobs exist, and that there is no error in the hash of those blobs.
        /// When these checks fail, an exception is raised, and the task fails.
        /// </summary>
        /// <param name="hashes">designates the blobs to be copied</param>
        /// <param name="accountId">the account of <paramref name="source"/> store. only helps with error messages</param>
        /// <returns></returns>
        public static async Task<WrittenBlob[]> ImportBlobs(
            this IWriteOnlyStore destination, 
            IReadOnlyStore source, 
            IReadOnlyCollection<Hash> hashes,
            long? accountId = null, 
            CancellationToken cancel = default)
        {
            var checkExistTasks = hashes.Select(
                h => source[h].ExistsAsync(cancel)
            ).ToArray();

            bool[] exist = await Task.WhenAll(checkExistTasks);

            int i = 0;
            foreach (var h in hashes)
            {
                if (!exist[i])
                {
                    throw new NoSuchBlobException( accountId?.ToString() ?? "<unknown>", h);
                }
            }

            var copyTasks = hashes.Select(
                h => destination.ImportBlobUnchecked( source[h], cancel )).ToArray();

            var writtenBlobs = await Task.WhenAll(copyTasks).ConfigureAwait(false);

            List<(Hash orig, Hash result)> badHashes = null;
            i = 0;
            foreach (var orig in hashes)
            {
                var blob = writtenBlobs[i++];
                if ( !blob.Hash.Equals(orig) )
                {
                    badHashes = badHashes ?? new List<(Hash orig, Hash result)>();
                    badHashes.Add((orig, blob.Hash));
                }
            }

            if (badHashes != null)
            {
                string details = string.Join("\n", badHashes.Select(_ => $"  {accountId}/{_.orig}: {_.result}"));
                throw new Exception("Bad hash(es) detected: " + details);
            }

            return writtenBlobs;
        }

        private static async Task<WrittenBlob> ImportBlobUnchecked(this IWriteOnlyStore store, IReadBlobRef srcBlob, CancellationToken cancel)
        {
            using (var writer = store.StartWriting())
            {
                using (var reader = await srcBlob.OpenAsync(cancel).ConfigureAwait(false))
                {
                    await writer.WriteAsync(reader.ReadAsync, null, cancel).ConfigureAwait(false);
                }

                return await writer.CommitAsync(cancel).ConfigureAwait(false);
            }
        }
    }
}