using System;
using System.Runtime.Serialization;

namespace Lokad.ContentAddr.Azure
{
    [DataContract]
    public class AzureDeletedBlobInfo
    {
        public static readonly string Gdpr = "GDPR";

        /// <summary>
        /// Date when the deleted blob was originally created
        /// </summary>
        [DataMember]  public DateTime Created { get; set; }
        /// <summary>
        /// Date when the blob was deleted
        /// </summary>
        [DataMember]  public DateTime Deleted { get; set; }
        /// <summary>
        /// A string containing the reason for the deletion (human-readable)
        /// </summary>
        [DataMember]  public string Reason { get; set; }
        /// <summary>
        /// Size of the blob that was deleted
        /// </summary>
        [DataMember] public long Size { get; set; }
    }
}
