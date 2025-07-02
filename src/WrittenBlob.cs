namespace Lokad.ContentAddr;

/// <summary> A written blob. </summary>
/// <param name="Hash"> The hash of the written blob. </param>
/// <param name="Size"> The size of the written blob, in bytes. </param>
public readonly record struct WrittenBlob(Hash Hash, long Size);