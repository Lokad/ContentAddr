using System.IO;

namespace Lokad.ContentAddr.Disk
{
    /// <summary> Methods for computing file and folder paths. </summary>
    internal static class DiskStorePaths
    {
        /// <summary> The path of a file containing the blob with the specified hash. </summary>
        /// <remarks>
        ///     To avoid having too many files in a directory, the path of a file
        ///     with hash ABCDEFGHIJKLMNOPQRSTUVWXYZ123456 is stored in a file with
        ///     relative path /_AB/CDEFGHIJKLMNOPQRSTUVWXYZ123456 (the initial '_' is
        ///     added to distinguish the splitting folders, and keep an alphabetic
        ///     sort in Windows Explorer).
        /// </remarks>
        internal static string PathOfHash(string path, Hash hash)
        {
            var hashstr = hash.ToString();
            return Path.Combine(
                Path.Combine(path, $"_{hashstr[0]}{hashstr[1]}"),
                hashstr.Substring(2));
        }

        /// <summary>
        /// The path of the folder containing all blobs from a specified account.
        /// </summary>
        internal static string PathOfAccount(string rootPath, long account) =>
            Path.Combine(rootPath, account.ToString("D"));

        /// <summary>
        /// The path fo a file containing the blob with the specified hash and account.
        /// </summary>
        internal static string PathFromRoot(string rootPath, long account, Hash hash) =>
            PathOfHash(PathOfAccount(rootPath, account), hash);
    }
}
