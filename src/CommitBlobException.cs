using System;

namespace Lokad.ContentAddr
{
    /// <see cref="AzureStore.CommitTemporaryBlob"/>
    public sealed class CommitBlobException : Exception
    {
        public CommitBlobException(string realm, string name, string message) :
            base($"'{name}' -> '{realm}': {message}")
        { }
    }
}