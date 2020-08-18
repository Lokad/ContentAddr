using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lokad.ContentAddr.Disk
{
    /// <summary> References a blob stored in an on-disk file. </summary>
    public sealed class DiskBlobRef : IReadBlobRef
    {
        /// <summary> The path of the file. </summary>
        private readonly string _path;

        public DiskBlobRef(Hash hash, string path)
        {
            _path = path;
            Hash = hash;
        }

        /// <see cref="IReadBlobRef.Hash"/>
        public Hash Hash { get; }

        /// <see cref="IReadBlobRef.ExistsAsync"/>
        public Task<bool> ExistsAsync(CancellationToken cancel) =>
            Task.FromResult(File.Exists(_path));

        /// <see cref="IReadBlobRef.GetSizeAsync"/>
        public Task<long> GetSizeAsync(CancellationToken cancel)
        {
            var info = new FileInfo(_path);
            if (!info.Exists)
                throw new NoSuchBlobException(Path.GetDirectoryName(Path.GetDirectoryName(_path)), Hash);

            return Task.FromResult(info.Length);
        }

        /// <see cref="IReadBlobRef.OpenAsync"/>
        public Task<Stream> OpenAsync(CancellationToken cancel)
        {
            try
            {
                var stream = File.Open(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return Task.FromResult<Stream>(stream);
            }
            catch (FileNotFoundException)
            {
                throw new NoSuchBlobException(Path.GetDirectoryName(Path.GetDirectoryName(_path)), Hash);
            }
        }
    }
}