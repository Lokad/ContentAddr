using System.IO;
using P = System.IO.Path;

namespace Lokad.ContentAddr.Disk
{
    /// <summary> Stores files in a directory. </summary>
    /// <see cref="DiskReadOnlyStore"/>
    public sealed class DiskStore : DiskReadOnlyStore, IStore<IReadBlobRef>
    {
        public DiskStore(string path) : base(path)
        {
            Directory.CreateDirectory(path);
        }

        /// <see cref="IStore{TBlobRef}.StartWriting"/>
        public StoreWriter StartWriting() => new DiskWriter(Path);

        /// <summary> Delete the blob for the specified hash. </summary>
        /// <remarks> Disks do not have infinite storage space, so deleting values is necessary. </remarks>
        public void Delete(Hash hash)
        {
            var path = DiskStorePaths.PathOfHash(Path, hash);
            if (!File.Exists(path)) return;

            try
            {
                File.Delete(path);
            }
            catch (IOException) when (!File.Exists(path))
            {
                // Another writer raced us to delete the destination file.
                // So, there's nothing left for us to do.
            }

            var directory = P.GetDirectoryName(path);
            if (directory == null) return;

            try
            {
                Directory.Delete(path);
            }
            catch (IOException)
            {
                // There is no thread-safe way to check whether a directory is 
                // empty, and then delete it (there's always the possibility for
                // another thread to create a file in that directory between the
                // empty-check and the deletion), so all we can do is try to delete
                // it (in non-recursive mode, so that it can only be dropped if it
                // is empty) and ignore the exception thrown if it contains a file.
            }
        }
    }
}
