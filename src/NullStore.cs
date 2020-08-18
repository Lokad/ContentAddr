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
            /// <see cref="StoreWriter.DoWriteAsync"/>
            protected override Task DoWriteAsync(byte[] buffer, int offset, int count, CancellationToken cancel) =>
                Task.FromResult(0);

            /// <see cref="StoreWriter.DoCommitAsync"/>
            protected override Task DoCommitAsync(Hash hash, CancellationToken cancel) =>
                Task.FromResult(0);
        }
    }
}
