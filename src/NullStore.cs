using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr
{
    /// <summary> A store that discards everything written to it. </summary>
    public sealed class NullStore : IWriteOnlyStore
    {
        public StoreWriter StartWriting() => new NullWriter();

        private sealed class NullWriter : StoreWriter
        {
            /// <inheritdoc cref="StoreWriter.DoWriteAsync"/>
            protected override Task DoWriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancel) =>
                Task.FromResult(0);

            /// <inheritdoc cref="StoreWriter.DoCommitAsync"/>
            protected override Task DoCommitAsync(Hash hash, CancellationToken cancel) =>
                Task.FromResult(0);
        }
    }
}
