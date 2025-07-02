using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr;

/// <summary> Base class for writers. </summary>
/// <see cref="IStore{TReadBlob}.StartWriting"/>
/// <remarks>
///     A writer is expected to receive an ordered sequence of byte blocks. These
///     bytes should be written to the store consecutively as a single blob. When
///     <see cref="CommitAsync"/> is called, the writer can no longer be written to,
///     but the blob is persistently stored and its hash and size are returned.
/// </remarks>
public abstract class StoreWriter : IDisposable
{
    /// <summary> Becomes true once a commit has completed successfully.</summary>
    /// <remarks>
    ///     Call <see cref="CommitAsync"/> (which is idempotent) to retrieve the 
    ///     <see cref="WrittenBlob"/> that was committed.
    /// </remarks>
    public bool IsCommitted { get; private set; }

    /// <summary> If <see cref="IsCommitted"/> is true, the blob that was committed. </summary>
    public WrittenBlob CommittedBlob
    {
        get
        {
            if (Failure != null)
                throw new InvalidOperationException(
                    "StoreWriter is in a failed state due to an earlier exception.",
                    Failure);

            if (!IsCommitted)
                throw new InvalidOperationException("StoreWriter not committed yet.");

            return new WrittenBlob(_hash, Size);
        }
    }

    /// <summary> Becomes true once a commit is requested. </summary>
    /// <remarks>
    /// <para>
    ///     Becomes true when either <see cref="CommitAsync"/> or 
    ///     <see cref="WriteAndCommitAsync"/> is called and returns. 
    /// </para><para>
    ///     Once true, any calls to <see cref="CommitAsync"/> return the 
    ///     same task as the call that set this field to true, and all calls
    ///     that perform writes will throw. 
    /// </para><para>
    ///     Once the commit task completes, either <see cref="IsCommitted"/> will be true
    ///     or <see cref="Failure"/> will be non-null, but this field remains true.
    /// </para>
    /// </remarks>
    public bool CommitWasRequested => _theCommit != null;

    /// <summary> If non-null, the exception that caused a write or commit to fail. </summary>
    public Exception Failure { get; private set; }

    /// <summary> The number of bytes received by this writer so far. </summary>
    /// <remarks> 
    /// <para>
    ///     The meaning of this value during a call to <see cref="DoWriteAsync"/> is
    ///     not well-defined: it will be *larger* than the bytes written so far, but 
    ///     there is nothing more precise than that.
    /// </para><para>
    ///     When a method returns (even if its returned task has not completed yet), this
    ///     field is equal to the number of bytes pushed to this writer so far, including
    ///     both sync and async methods.
    /// </para>
    /// </remarks>
    public long Size => _syncOffset + _hashedBytes;

    /// <summary> All bytes that were passed to the <see cref="_hasher"/> so far. </summary>
    private long _hashedBytes;

    /// <summary> The hash of the result, once committed. </summary>
    private Hash _hash;

    /// <summary> Used to hash incoming bytes. </summary>
    private readonly IncrementalHash _hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

    /// <summary> Clears the hasher contents and returns the computed hash. </summary>
    private Hash GetHashFromHasher()
    {
        Span<byte> destination = stackalloc byte[Hash.Size];
        _hasher.TryGetHashAndReset(destination, out _);
        return Hash.FromBytes(destination);
    }

    /// <summary> The task returned by the first call to a commit function. </summary>
    private Task<WrittenBlob> _theCommit;

    /// <summary> Commit the blob to the store. </summary>
    /// <remarks>
    ///     Calling this function more than once returns the same value.
    ///     Once a blob is committed, the writer no longer supports calls
    ///     to <see cref="WriteAsync(byte[],int,int,CancellationToken)"/>
    ///     or to <see cref="WriteAsync(Func{byte[],int,int,CancellationToken,Task{int}},long?,CancellationToken)"/> 
    /// </remarks>        
    public Task<WrittenBlob> CommitAsync(CancellationToken cancel) =>
        _theCommit ?? (_theCommit = RealCommitAsync(cancel));

    /// <summary> Async implementation behind <see cref="CommitAsync"/> </summary>
    /// <remarks> Will only be called if <see cref="_theCommit"/> is null. </remarks>
    public async Task<WrittenBlob> RealCommitAsync(CancellationToken cancel)
    {
        if (Failure != null)
            throw new InvalidOperationException(
                "StoreWriter is in a failed state due to an earlier exception.",
                Failure);

        if (_syncBuffer != null)
        {
            // If there are unwritten sync bytes, this is the time to send them out.
            // Since we've been asked to commit, this is a good opportunity for a
            // write-then-commit optimization.

            var buffer = _syncBuffer;
            var count = _syncOffset;

            _syncBuffer = null;
            _syncOffset = 0;
            
            return await RealWriteAndCommitAsync(buffer.AsMemory(0, count), cancel).ConfigureAwait(false);
        }
        
        try
        {
            _hash = GetHashFromHasher();

            await DoOptCommitAsync(_hash, null, cancel).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Failure = e;
            throw;
        }

        IsCommitted = true;
    
        return CommittedBlob;
    }

    #region Sync writing

    /// <summary> Buffer for <see cref="Write"/>. </summary>
    private byte[] _syncBuffer;

    /// <summary> Offset within <see cref="_syncBuffer"/>. </summary>
    private int _syncOffset;

    /// <summary> Synchronous write. </summary>
    /// <remarks>
    /// <para>
    ///     Some outer interfaces (such as <see cref="System.IO.Stream"/>) require us to support 
    ///     synchronous writes. We cannot just wrap the async write function with a <c>Wait()</c>
    ///     due to deadlock risks, so we provide this method instead. What it does is: 
    /// </para><para>
    ///       - every time data is received, it is appended to the <see cref="_syncBuffer"/>
    ///         (allocated on first use) at position <see cref="_syncOffset"/>. This is because
    ///         we are not allowed to keep around the buffer received as argument, so we need 
    ///         to perform a copy. The good news is, this is actually more efficient when dealing
    ///         with a lot of very small writes.
    /// </para><para>
    ///       - when the buffer is full, or when an async operation is performed through 
    ///         another method, <see cref="WriteSyncBuffer"/> is called to perform a 
    ///         <see cref="WriteAsync(byte[],int,int,CancellationToken)"/> with the buffer
    ///         beforehand. This call is not awaited.
    /// </para>
    /// </remarks>
    public void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    /// <summary> Synchronous write. </summary>
    /// <remarks>
    /// <para>
    ///     Some outer interfaces (such as <see cref="System.IO.Stream"/>) require us to support 
    ///     synchronous writes. We cannot just wrap the async write function with a <c>Wait()</c>
    ///     due to deadlock risks, so we provide this method instead. What it does is: 
    /// </para><para>
    ///       - every time data is received, it is appended to the <see cref="_syncBuffer"/>
    ///         (allocated on first use) at position <see cref="_syncOffset"/>. This is because
    ///         we are not allowed to keep around the buffer received as argument, so we need 
    ///         to perform a copy. The good news is, this is actually more efficient when dealing
    ///         with a lot of very small writes.
    /// </para><para>
    ///       - when the buffer is full, or when an async operation is performed through 
    ///         another method, <see cref="WriteSyncBuffer"/> is called to perform a 
    ///         <see cref="WriteAsync(byte[],int,int,CancellationToken)"/> with the buffer
    ///         beforehand. This call is not awaited.
    /// </para>
    /// </remarks>
    public void Write(ReadOnlySpan<byte> span)
    {
        if (Failure != null)
            throw new InvalidOperationException(
                "StoreWriter is in a failed state due to an earlier exception.",
                Failure);

        if (CommitWasRequested)
            throw new InvalidOperationException("Cannot write to a committed StoreWriter.");

        while (span.Length > 0)
        {
            _syncBuffer ??= new byte[4 * 1024 * 1024];

            var length = Math.Min(span.Length, _syncBuffer.Length - _syncOffset);
            span[..length].CopyTo(_syncBuffer.AsSpan(_syncOffset));
                
            _syncOffset += length;
            span = span[length..];

            if (_syncOffset == _syncBuffer.Length) WriteSyncBuffer(CancellationToken.None);
        }
    }

    /// <summary> Single-byte equivalent of <see cref="Write"/>. </summary>
    public void WriteByte(byte b)
    {
        if (Failure != null)
            throw new InvalidOperationException(
                "StoreWriter is in a failed state due to an earlier exception.",
                Failure);

        if (CommitWasRequested)
            throw new InvalidOperationException("Cannot write to a committed StoreWriter.");
        
        _syncBuffer ??= new byte[4 * 1024 * 1024];
        _syncBuffer[_syncOffset++] = b;
        if (_syncOffset == _syncBuffer.Length) WriteSyncBuffer(CancellationToken.None);
    }

    /// <summary>
    /// Write all the contents of <see cref="_syncBuffer"/>, then drop all 
    /// pending sync data.
    /// </summary>
    public void WriteSyncBuffer(CancellationToken cancel)
    {
        var buffer = _syncBuffer;
        var count = _syncOffset;

        _syncOffset = 0;
        _syncBuffer = null;
        
        // The thread-unsafe parts of this call are done by the time it returns. The
        // task itself only needs to be awaited in order to know when the buffer is 
        // no longer in use ; but since we discard the buffer, we don't care, so there
        // is no need to wait. 
        //
        // In practice, the various implementations of StoreWriter will deal with all
        // ongoing writes, so that they are all completed by the time the final commit
        // is performed.

        _ = WriteAsync(buffer, 0, count, cancel);
    }

    #endregion

    /// <summary> Write to the background task. </summary>
    public Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
        WriteAsync(buffer.AsMemory(offset, count), cancel);

    /// <summary> Write to the background task. </summary>
    public async Task WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
    {
        if (Failure != null)
            throw new InvalidOperationException(
                "StoreWriter is in a failed state due to an earlier exception.",
                Failure);

        if (CommitWasRequested)
            throw new InvalidOperationException("Cannot write to a committed StoreWriter.");

        if (_syncBuffer != null)
            WriteSyncBuffer(cancel);

        _hasher.AppendData(buffer.Span);
        _hashedBytes += buffer.Length;

        try
        {
            await DoWriteAsync(buffer, cancel).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Failure = e;
            throw;
        }
    }

    /// <summary>
    ///     Equivalent to calling <see cref="WriteAsync(byte[],int,int,CancellationToken)"/> followed 
    ///     by <see cref="CommitAsync"/>, but allows the <see cref="StoreWriter"/> to perform 
    ///     optimizations during the write because it knows it will be the last.
    /// </summary>
    public Task<WrittenBlob> WriteAndCommitAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
        WriteAndCommitAsync(buffer.AsMemory(offset, count), cancel);

    public Task<WrittenBlob> WriteAndCommitAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
    {
        if (_theCommit != null)
            throw new InvalidOperationException("Cannot write to a committed StoreWriter.");

        if (Failure != null)
            throw new InvalidOperationException(
                "StoreWriter is in a failed state due to an earlier exception.",
                Failure);

        return _theCommit = RealWriteAndCommitAsync(buffer, cancel);
    }

    /// <summary> Async implementation of <see cref="WriteAndCommitAsync"/>. </summary>
    /// <remarks> Will only be called if <see cref="_theCommit"/> is null. </remarks>
    private async Task<WrittenBlob> RealWriteAndCommitAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel)
    {
        // We let the protected async function decide whether the final writes need 
        // to be performed (if the blob does not exist yet), by providing a closure 
        // that finishes the remaining writes.
        Func<Task> writeIfNecessary;

        if (_syncBuffer == null)
        {
            writeIfNecessary = () => DoWriteAsync(buffer, cancel);
        }
        else
        {
            // There is sync data to be written: hash it first (before the argument
            // buffer is hashed), then generate a 'writeIfNecessary' that writes 
            // both the sync data and the argument buffer.
            var preBuffer = _syncBuffer;
            var preCount = _syncOffset;

            _syncBuffer = null;
            _syncOffset = 0;

            _hasher.AppendData(preBuffer.AsSpan(0, preCount));
            _hashedBytes += preCount;

            writeIfNecessary = () =>
            {
                // As long as the calls are done in sequence, the returned tasks
                // themselves can be awaited in parallel.
                var pre = DoWriteAsync(preBuffer.AsMemory(0, preCount), cancel);
                var post = DoWriteAsync(buffer, cancel);
                return Task.WhenAll(pre, post);
            };
        }

        _hasher.AppendData(buffer.Span);
        _hashedBytes += buffer.Length;

        _hash = GetHashFromHasher();

        try
        {
            await DoOptCommitAsync(_hash, writeIfNecessary, cancel).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Failure = e;
            throw;
        }

        IsCommitted = true;

        return CommittedBlob;
    }

    /// <summary> Copy from an asynchronous reader. </summary>
    /// <param name="readAsync">
    ///     Reader function. <c>readAsync(buffer, offset, count, cancel)</c> should read bytes into
    ///     section of <c>buffer</c> defined by the offset and count, and return the number of 
    ///     bytes read. Should read at least one byte, unless there are no bytes left in the
    ///     stream represented by the reader, at which point it should return zero.
    /// </param>
    /// <param name="maxBytes"> 
    ///     Maximum number of bytes to read, in total. Used to truncate streams.
    /// </param>
    /// <param name="cancel"> Cancellation token. </param>
    public async Task WriteAsync(
        Func<byte[], int, int, CancellationToken, Task<int>> readAsync,
        long? maxBytes,
        CancellationToken cancel)
    {
        const int maxBufferSize = 4 * 1024 * 1024;
        var bufferSize = maxBytes == null ? maxBufferSize : Math.Min(maxBytes.Value, maxBufferSize);

        byte[] writeBuffer = null, readBuffer = null; // The buffers for reading and writing.

        Task writing = Task.FromResult(0); // This task completes once the 'writeBuffer' is again free for use.

        var total = 0L; // Total bytes copied so far

        // Once 'readAsync()' returns zero, it has reached end-of-stream forever, so we 
        // do not attempt to read from it ever again.
        var readReturnedZero = false;

        while (true)
        {
            var offset = 0; // Current offset within 'readBuffer'

            while (!readReturnedZero)
            {
                var limit = maxBufferSize - offset;
                if (maxBytes != null && limit + total > maxBytes.Value)
                    limit = (int)(maxBytes.Value - total);

                if (limit == 0) break;

                if (readBuffer == null) readBuffer = new byte[bufferSize];

                var read = await readAsync(readBuffer, offset, limit, cancel).ConfigureAwait(false);

                if (read == 0)
                    readReturnedZero = true;

                offset += read;
                total += read;
            }

            // Wait for previous write to complete before starting a new one, or 
            // exiting the loop.       
            await writing.ConfigureAwait(false);

            // This happens if no data was read during the previous pass, which 
            // can only happen once the end of the stream was reached (either
            // the true end, or the maxBytes).
            if (offset == 0) return;

            // Swap buffers. Since we awaited 'writing' before, the 'writeBuffer'
            // (which becomes the 'readBuffer') is no longer in use by the write
            // task. 
            (readBuffer, writeBuffer) = (writeBuffer, readBuffer);

            // Start writing. This makes the 'writeBuffer' no longer available.
            writing = WriteAsync(writeBuffer, 0, offset, cancel);
        }
    }
    
    /// <summary> Persist the contents of a byte buffer. </summary>
    /// <remarks> 
    ///     The returned task should complete as soon as another call to <see cref="DoWriteAsync"/>
    ///     or <see cref="DoCommitAsync"/> can be performed, even if the data is not yet fully persisted.
    /// </remarks>
    protected abstract Task DoWriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel);

    /// <summary> Commit all data written so far, with the provided hash. </summary>
    /// <remarks> The returned task should complete as soon as the data is safely persisted. </remarks>
    /// <param name="hash"> The hash of the data. </param>
    /// <param name="optionalWrite">
    /// <para>
    ///     Given the possibility of generating the same blob more than once, the store writer
    ///     can choose to delay the (costly) writing of some parts of the data until the 
    ///     <see cref="DoCommitAsync"/> function can get a chance to check whether a blob with the
    ///     same hash already exists.
    /// </para><para>
    ///     If the blob already exists, then this parameter can be safely ignored, whatever its value
    ///     is. 
    /// </para><para>
    ///     If the blob does not exist, and this parameter is not null, then <see cref="DoCommitAsync"/> 
    ///     should call the function and await the returned task, and THEN perform the commit. Calling
    ///     the function will, in all likelihood, call <see cref="DoWriteAsync"/>, so make sure the
    ///     proper re-entrance patterns are enforced.
    /// </para><para>
    ///     This function is virtual, not abstract. The default implementation just executes the optional 
    ///     write and forwards the call to the abstract variant.
    /// </para>
    /// </param>
    /// <param name="cancel"> Cancellation token. </param>
    protected virtual async Task DoOptCommitAsync(Hash hash, Func<Task> optionalWrite, CancellationToken cancel)
    {
        if (optionalWrite != null) await optionalWrite().ConfigureAwait(false);
        await DoCommitAsync(hash, cancel).ConfigureAwait(false);
    }

    /// <summary> Commit all data written so far, with the provided hash. </summary>
    /// <remarks> The returned task should complete as soon as the data is safely persisted. </remarks>
    protected abstract Task DoCommitAsync(Hash hash, CancellationToken cancel);
    
    public virtual void Dispose() {}
}