using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Foca.Metadata.Diagrams;
using Foca.Metadata.Extractors;
using Xunit;
using Path = System.IO.Path;

namespace Foca.Metadata.Tests
{
    /// <summary>
    /// FOCA analyses documents downloaded from servers it does not control. Every extractor must finish
    /// quickly, within bounded memory, without reading local files or the network, whatever the input.
    /// </summary>
    public class HostileDocumentTests
    {
        private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

        private static FileMetadata Analyze(string extension, byte[] data)
        {
            Task<FileMetadata> task = Task.Run(() =>
            {
                using (var ms = new MemoryStream(data))
                using (DocumentExtractor doc = DocumentExtractor.Create(extension, ms))
                    return doc.AnalyzeFile();
            });
            Assert.True(task.Wait(Limit), $"{extension}: analysis did not finish (hang or exponential work)");
            return task.Result;
        }

        private static byte[] Zip(params (string Name, byte[] Data)[] entries)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    foreach ((string name, byte[] data) in entries)
                    {
                        using (Stream s = zip.CreateEntry(name, CompressionLevel.Optimal).Open())
                            s.Write(data, 0, data.Length);
                    }
                }
                return ms.ToArray();
            }
        }

        private const string BillionLaughs =
            "<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\">" +
            "<!ENTITY lol1 \"&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;\">" +
            "<!ENTITY lol2 \"&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;\">" +
            "<!ENTITY lol3 \"&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;\">" +
            "<!ENTITY lol4 \"&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;\">" +
            "<!ENTITY lol5 \"&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;\">" +
            "<!ENTITY lol6 \"&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;\">" +
            "<!ENTITY lol7 \"&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;\">" +
            "<!ENTITY lol8 \"&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;\">" +
            "<!ENTITY lol9 \"&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;\">]>";

        [Fact]
        public void EntityExpansionInSvgIsNotPerformed()
        {
            FileMetadata m = Analyze(".svg", Encoding.UTF8.GetBytes(BillionLaughs + "<svg><title>&lol9;</title></svg>"));
            Assert.NotNull(m);
        }

        [Fact]
        public void EntityExpansionInOpenDocumentMetadataIsNotPerformed()
        {
            byte[] meta = Encoding.UTF8.GetBytes(BillionLaughs + "<office:document-meta xmlns:office=\"o\" xmlns:dc=\"d\"><dc:creator>&lol9;</dc:creator></office:document-meta>");
            FileMetadata m = Analyze(".odt", Zip(("meta.xml", meta)));
            Assert.DoesNotContain(m.Users, u => u.Value.Contains("lollol"));
        }

        [Fact]
        public void ExternalEntitiesAreNotResolved()
        {
            string secret = Path.Combine(Path.GetTempPath(), "foca-xxe-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(secret, "C:\\Users\\leaked.secret\\");
            try
            {
                string uri = new Uri(secret).AbsoluteUri;
                byte[] svg = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM \"{uri}\">]><svg><desc>&xxe;</desc><a href=\"&xxe;\"/></svg>");
                FileMetadata m = Analyze(".svg", svg);
                Assert.DoesNotContain(m.Paths, p => p.Value.Contains("leaked.secret"));
                Assert.DoesNotContain(m.Users, u => u.Value.Contains("leaked.secret"));
            }
            finally
            {
                File.Delete(secret);
            }
        }

        [Fact]
        public void GzipBombInSvgzIsRejected()
        {
            byte[] bomb;
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                {
                    byte[] chunk = Encoding.ASCII.GetBytes("<svg>" + new string(' ', 1024 * 1024));
                    gz.Write(chunk, 0, chunk.Length);
                    byte[] spaces = new byte[1024 * 1024];
                    for (int i = 0; i < spaces.Length; i++)
                        spaces[i] = (byte)' ';
                    for (int i = 0; i < 80; i++)   // 80 MB of whitespace, a few KB compressed
                        gz.Write(spaces, 0, spaces.Length);
                }
                bomb = ms.ToArray();
            }
            Assert.True(bomb.Length < 1024 * 1024);
            Assert.NotNull(Analyze(".svgz", bomb));
        }

        [Fact]
        public void CompressedSvgIsAnalysed()
        {
            byte[] svg = Corpus.Get("sample.svg");
            byte[] svgz;
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                    gz.Write(svg, 0, svg.Length);
                svgz = ms.ToArray();
            }
            Assert.Contains(Analyze(".svgz", svgz).Users, u => u.Value == "ivan.designer");
        }

        [Fact]
        public void ZipBombImageInsideOoxmlIsBounded()
        {
            byte[] docx = Corpus.Get("sample.docx");
            byte[] bombed;
            using (var ms = new MemoryStream())
            {
                ms.Write(docx, 0, docx.Length);
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, true))
                using (Stream s = zip.CreateEntry("word/media/huge.jpeg", CompressionLevel.Optimal).Open())
                {
                    byte[] zeros = new byte[1024 * 1024];
                    for (int i = 0; i < 80; i++)   // 80 MB inflated
                        s.Write(zeros, 0, zeros.Length);
                }
                bombed = ms.ToArray();
            }
            FileMetadata m = Analyze(".docx", bombed);
            // Core properties are still extracted; the oversized part is skipped
            Assert.Contains(m.Users, u => u.Value == "alice.author");
        }

        [Fact]
        public void WordPerfectWithManyMarkersStaysLinear()
        {
            // Header + 2 MB of string markers: previously quadratic (the rest of the file was copied per marker)
            byte[] data = new byte[2 * 1024 * 1024];
            data[0] = 0xFF; data[1] = (byte)'W'; data[2] = (byte)'P'; data[3] = (byte)'C';
            for (int i = 16; i + 1 < data.Length; i += 4)
                data[i + 1] = 0x98;
            Assert.NotNull(Analyze(".wpd", data));
        }

        [Fact]
        public void OleLengthFieldsCannotForceHugeAllocations()
        {
            // A valid compound file whose WordDocument fields claim gigabyte-sized tables
            byte[] data = (byte[])Corpus.Get("cyclic-chain.doc").Clone();
            for (int i = 2048 - 64; i < 2048; i++)
                data[i] = 0xFF;
            Assert.NotNull(Analyze(".doc", data));
        }

        /// <summary>
        /// Deterministic mutation fuzzing of every corpus document: bit flips, byte runs, truncation and
        /// duplication. Each mutant must be analysed within the time limit.
        /// </summary>
        [Theory]
        [InlineData("sample.docx", ".docx")]
        [InlineData("sample.odt", ".odt")]
        [InlineData("sample.pdf", ".pdf")]
        [InlineData("sample.svg", ".svg")]
        [InlineData("sample.jpg", ".jpg")]
        [InlineData("sample.rdp", ".rdp")]
        [InlineData("cyclic-chain.doc", ".doc")]
        public void MutatedDocumentsNeverHang(string file, string extension)
        {
            byte[] original = Corpus.Get(file);
            var random = new Random(file.GetHashCode() & 0x7FFF ^ 1234);
            int iterations = 150;
            for (int n = 0; n < iterations; n++)
            {
                byte[] mutant = Mutate(original, random);
                Assert.NotNull(Analyze(extension, mutant));
            }
        }

        private static byte[] Mutate(byte[] input, Random random)
        {
            byte[] data = (byte[])input.Clone();
            switch (random.Next(5))
            {
                case 0: // flip bits
                    for (int i = 0; i < 1 + random.Next(16); i++)
                        data[random.Next(data.Length)] ^= (byte)(1 << random.Next(8));
                    break;
                case 1: // overwrite a run with an interesting value
                    byte value = new byte[] { 0x00, 0xFF, 0x7F, 0x80 }[random.Next(4)];
                    int start = random.Next(data.Length);
                    for (int i = start; i < Math.Min(data.Length, start + 1 + random.Next(64)); i++)
                        data[i] = value;
                    break;
                case 2: // truncate
                    Array.Resize(ref data, random.Next(1, data.Length));
                    break;
                case 3: // duplicate a block
                    int from = random.Next(data.Length);
                    int len = Math.Min(data.Length - from, 1 + random.Next(512));
                    data = data.Take(from + len).Concat(data.Skip(from).Take(len)).Concat(data.Skip(from + len)).ToArray();
                    break;
                default: // random bytes in the middle
                    int at = random.Next(data.Length);
                    for (int i = at; i < Math.Min(data.Length, at + 1 + random.Next(32)); i++)
                        data[i] = (byte)random.Next(256);
                    break;
            }
            return data;
        }
    }
}
