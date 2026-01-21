using Lokad.ContentAddr.Memory;


namespace Lokad.ContentAddr.Tests;

public class MemoryTests : UploadFixture
{
    public MemoryTests()
    {
        Store = new MemoryStore();
    }
}
