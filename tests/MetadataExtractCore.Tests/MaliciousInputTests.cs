using System;
using System.IO;
using System.Threading.Tasks;
using MetadataExtractCore.Diagrams;
using MetadataExtractCore.Extractors;
using MetadataExtractCore.Utilities;
using Xunit;
using Path = System.IO.Path;

namespace MetadataExtractCore.Tests
{
    /// <summary>
    /// Documents come from untrusted servers; crafted files must fail fast instead of hanging FOCA.
    /// </summary>
    public class MaliciousInputTests
    {
        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

        private static T WithinLimit<T>(Func<T> action)
        {
            Task<T> task = Task.Run(action);
            Assert.True(task.Wait(Limit), "Analysis did not finish: possible infinite loop.");
            return task.Result;
        }

        [Fact]
        public void CyclicOleSectorChainTerminates()
        {
            byte[] data = Corpus.Get("cyclic-chain.doc");
            FileMetadata metadata = WithinLimit(() =>
            {
                using (var ms = new MemoryStream(data))
                using (DocumentExtractor doc = DocumentExtractor.Create(".doc", ms))
                    return doc.AnalyzeFile();
            });
            Assert.NotNull(metadata);
        }

        [Fact]
        public void CyclicOleFormatDetectionTerminates()
        {
            byte[] data = Corpus.Get("cyclic-chain.doc");
            string format = WithinLimit(() =>
            {
                using (var ms = new MemoryStream(data))
                    return Functions.GetFormatFile(ms);
            });
            Assert.NotEqual(".doc", format);
        }

        [Fact]
        public void OleHeaderWithAbsurdSectorSizeIsRejected()
        {
            byte[] data = (byte[])Corpus.Get("cyclic-chain.doc").Clone();
            data[30] = 40; // sector shift 2^40
            using (var ms = new MemoryStream(data))
                Assert.False(new OleDocument(ms).isValid());
        }

        [Fact]
        public void TruncatedDocumentsDoNotThrow()
        {
            foreach (string name in new[] { "sample.docx", "sample.odt", "sample.pdf", "cyclic-chain.doc" })
            {
                byte[] full = Corpus.Get(name);
                byte[] half = new byte[full.Length / 2];
                Array.Copy(full, half, half.Length);
                FileMetadata metadata = WithinLimit(() =>
                {
                    using (var ms = new MemoryStream(half))
                    using (DocumentExtractor doc = DocumentExtractor.Create(Path.GetExtension(name), ms))
                        return doc.AnalyzeFile();
                });
                Assert.NotNull(metadata);
            }
        }
    }
}
