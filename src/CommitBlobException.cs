using System;

namespace Lokad.ContentAddr;

public sealed class CommitBlobException : Exception
{
    public CommitBlobException(string realm, string name, string message) :
        base($"'{name}' -> '{realm}': {message}")
    { }
}