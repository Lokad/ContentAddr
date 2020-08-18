using System;

namespace Lokad.ContentAddr
{
    /// <summary> Thrown when accessing a non-existent blob. </summary>
    public sealed class NoSuchBlobException : Exception
    {
        public Hash Hash { get; }

        public NoSuchBlobException(string realm, Hash hash, string location = null) : 
            base($"Blob {hash} not found in realm '{realm}'."   
                 + location == null ? "\nAt: " + location : "")
        {
            Hash = hash;
        }
    }
}