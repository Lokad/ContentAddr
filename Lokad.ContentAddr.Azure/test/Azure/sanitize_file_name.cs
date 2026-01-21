using Lokad.ContentAddr.Azure;
using Xunit;

namespace Lokad.ContentAddr.Tests.Azure
{
    public sealed class sanitize_file_name
    {
        [Theory]
        [InlineData("", "data", null)]
        [InlineData(".xlsx", "data.xlsx", null)]
        [InlineData("file.*.tsv", "file.-.tsv", null)]
        [InlineData("|/the*<-file.tsv.", "the-file.tsv", null)]
        [InlineData("données.tsv", "data.tsv", "donn%c3%a9es.tsv")]
        [InlineData("ünïcödé.tsv.gz", "data.csv.gz", "%c3%bcn%c3%afc%c3%b6d%c3%a9.tsv.gz")]
        [InlineData("with späce.tsv.gz", "data.csv.gz", "with%20sp%c3%a4ce.tsv.gz")]
        [InlineData("with space.tsv.gz", "with space.tsv.gz", null)]
        public void sanitize(string name, string ascii, string utf8)
        {
            var (rascii, rutf8) = AzureBlobRef.SanitizeFileName(name);
            Assert.Equal(ascii, rascii);
            Assert.Equal(utf8, rutf8);
        }
    }
}
