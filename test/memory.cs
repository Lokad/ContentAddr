using Lokad.ContentAddr.Memory;
using Xunit;

namespace Lokad.ContentAddr.Tests
{    
    public class memory : UploadFixture
    {
        public memory()
        {
            Store = new MemoryStore();
        }
    }
}
