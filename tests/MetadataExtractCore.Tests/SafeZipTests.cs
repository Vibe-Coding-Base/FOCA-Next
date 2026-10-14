using System.IO;
using System.IO.Compression;
using MetadataExtractCore.Utilities;
using Xunit;

namespace MetadataExtractCore.Tests
{
    public class SafeZipTests
    {
        private static MemoryStream ZipWith(string name, byte[] content)
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            using (Stream s = zip.CreateEntry(name).Open())
                s.Write(content, 0, content.Length);
            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }

        [Fact]
        public void ReadEntryReturnsContent()
        {
            using (MemoryStream ms = ZipWith("meta.xml", new byte[] { 1, 2, 3 }))
            using (SafeZip zip = SafeZip.Open(ms))
            using (MemoryStream entry = zip.ReadEntry("meta.xml"))
            {
                Assert.True(zip.Contains("meta.xml"));
                Assert.False(zip.Contains("META.XML"));
                Assert.Equal(new byte[] { 1, 2, 3 }, entry.ToArray());
            }
        }

        [Fact]
        public void HighlyCompressedEntryAboveLimitIsRejected()
        {
            // 4 MB of zeros compresses to a few KB: a miniature zip bomb.
            using (MemoryStream ms = ZipWith("content.xml", new byte[4 * 1024 * 1024]))
            using (SafeZip zip = SafeZip.Open(ms, maxEntryBytes: 1024 * 1024))
            {
                Assert.Throws<InvalidDataException>(() => zip.ReadEntry("content.xml"));
            }
        }

        [Fact]
        public void OpenLeavesCallerStreamOpen()
        {
            using (MemoryStream ms = ZipWith("a", new byte[1]))
            {
                SafeZip.Open(ms).Dispose();
                Assert.True(ms.CanRead);
            }
        }

        [Fact]
        public void NonZipInputThrowsInvalidData()
        {
            using (var ms = new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }))
                Assert.Throws<InvalidDataException>(() => SafeZip.Open(ms));
        }

        [Theory]
        [InlineData("docx", ".docx")]
        [InlineData("odt", ".odt")]
        [InlineData("pdf", ".pdf")]
        public void GetFormatFileDetectsContainerType(string fixture, string expected)
        {
            byte[] data = Corpus.Get("sample." + fixture);
            using (var ms = new MemoryStream(data))
                Assert.Equal(expected, Functions.GetFormatFile(ms));
        }
    }
}
