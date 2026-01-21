using System;

namespace Lokad.ContentAddr.Azure
{
    public sealed class AzureDeletedBlobException : Exception
    {
        public AzureDeletedBlobInfo AzureDeletedBlobInfo { get; }

        public AzureDeletedBlobException(AzureDeletedBlobInfo azureDeletedBlobInfo, string realm, Hash hash, string location = null)
            : base(($"Blob {hash} not found in realm '{realm}' but was deleted ({azureDeletedBlobInfo.Reason})." + location == null) ? ("\nAt: " + location) : "")
        {
            AzureDeletedBlobInfo = azureDeletedBlobInfo;
        }
    }
}
