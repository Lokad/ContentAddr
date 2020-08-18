using System;
using System.IO;
using Lokad.ContentAddr.Disk;
using Xunit;

namespace Lokad.ContentAddr.Tests
{
    public sealed class disk : UploadFixture, IDisposable
    {
        /// <summary> Temporary directory where files are stored. </summary>
        private string _path;

        
        public disk()
        {
            _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));            
            Store = new DiskStore(_path);
        }

        public void Dispose()
        {
            Directory.Delete(_path, recursive: true);
        }
    }
}
