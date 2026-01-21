using System;

namespace Lokad.ContentAddr.Azure
{
    public class BlobCommitException : Exception
    {
        public readonly string TempUrl;
        public readonly string FinalUrl;

        public BlobCommitException(string temp, string final, Exception inner) : base($"Could not commit blob from '{temp}' to '{final}'", inner)
        {
            TempUrl = temp;
            FinalUrl = final;
        }
    }
}