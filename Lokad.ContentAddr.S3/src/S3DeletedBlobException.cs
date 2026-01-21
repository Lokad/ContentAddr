using System;

namespace Lokad.ContentAddr.S3
{
    public sealed class S3DeletedBlobException : Exception
    {
        public S3DeletedBlobInfo S3DeletedBlobInfo { get; }

        public S3DeletedBlobException(S3DeletedBlobInfo s3DeletedBlobInfo, string realm, Hash hash, string location = null)
            : base($"Blob {hash} not found in realm '{realm}' but was deleted ({s3DeletedBlobInfo.Reason})." + (location != null ? "\nAt: " + location : ""))
        {
            S3DeletedBlobInfo = s3DeletedBlobInfo;
        }
    }
}
