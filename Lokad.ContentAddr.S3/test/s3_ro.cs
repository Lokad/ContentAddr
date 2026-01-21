using Lokad.ContentAddr.S3;
using System;
using System.IO;

namespace Lokad.ContentAddr.S3.Tests
{
    public class s3_ro : UploadFixture, IDisposable
    {
        public static readonly string Config = File.ReadAllText("s3_connection.txt");

        private readonly S3StoreFactory _factory;

        public s3_ro()
        {
            _factory = new S3StoreFactory(Config, readOnly: false, testPrefix: Guid.NewGuid().ToString("N"));

            WriteStore = _factory.ForAccount(1);
            ReadStore = _factory.ReadOnlyForAccount(1);
        }

        public void Dispose()
        {
            try { _factory.Delete(); } catch { }
        }
    }
}
