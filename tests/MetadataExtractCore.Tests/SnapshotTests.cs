using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using MetadataExtractCore.Diagrams;
using MetadataExtractCore.Extractors;
using Xunit;
using Path = System.IO.Path;

namespace MetadataExtractCore.Tests
{
    /// <summary>
    /// Characterization tests: the extracted metadata of each fixture is rendered to a stable text form and
    /// compared with an approved snapshot under Snapshots/. A missing snapshot is written and the test fails,
    /// so new baselines are always reviewed before being committed. Set FOCA_UPDATE_SNAPSHOTS=1 to re-approve.
    /// </summary>
    public class SnapshotTests
    {
        public static IEnumerable<object[]> Cases() => new[]
        {
            new object[] { "docx", ".docx" },
            new object[] { "odt", ".odt" },
            new object[] { "pdf", ".pdf" },
            new object[] { "svg", ".svg" },
            new object[] { "rdp", ".rdp" },
            new object[] { "jpg", ".jpg" },
        };

        private static byte[] Build(string name)
        {
            switch (name)
            {
                case "docx": return Fixtures.Docx();
                case "odt": return Fixtures.Odt();
                case "pdf": return Fixtures.Pdf();
                case "svg": return Fixtures.Svg();
                case "rdp": return Fixtures.Rdp();
                case "jpg": return Fixtures.Jpeg();
                default: throw new ArgumentOutOfRangeException(nameof(name));
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ExtractedMetadataMatchesSnapshot(string name, string extension)
        {
            string actual;
            using (var input = new MemoryStream(Build(name)))
            using (DocumentExtractor extractor = DocumentExtractor.Create(extension, input))
            {
                actual = Render(extractor.AnalyzeFile());
            }

            string path = Path.Combine(SnapshotDir(), name + ".txt");
            bool update = Environment.GetEnvironmentVariable("FOCA_UPDATE_SNAPSHOTS") == "1";
            if (update || !File.Exists(path))
            {
                File.WriteAllText(path, actual, new UTF8Encoding(false));
                Assert.True(update, $"Snapshot created at {path}; review and commit it.");
                return;
            }

            string expected = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.Equal(expected, actual.Replace("\r\n", "\n"));
        }

        private static string SnapshotDir([CallerFilePath] string source = "")
        {
            string dir = Path.Combine(Path.GetDirectoryName(source), "Snapshots");
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static string Render(FileMetadata m, string indent = "")
        {
            var sb = new StringBuilder();
            void Line(string key, object value)
            {
                string s = value?.ToString();
                if (!string.IsNullOrEmpty(s) && s != "0")
                    sb.Append(indent).Append(key).Append(": ").Append(s.Replace("\r", "\\r").Replace("\n", "\\n")).Append('\n');
            }
            void List<T>(string key, IEnumerable<T> items) where T : MetadataValue
            {
                foreach (string v in items.Select(i => i.Value).OrderBy(v => v, StringComparer.Ordinal))
                    Line(key, v);
            }
            string Day(DateTime? d) => d?.ToString("yyyy-MM-dd");

            Line("Title", m.Title);
            Line("Subject", m.Subject);
            Line("Keywords", m.Keywords);
            Line("Description", m.Description);
            Line("Comments", m.Comments);
            Line("Category", m.Category);
            Line("Company", m.Company);
            Line("Language", m.Language);
            Line("Template", m.Template);
            Line("OperatingSystem", m.OperatingSystem);
            Line("Model", m.Model);
            Line("UserInfo", m.UserInfo);
            Line("Statistic", m.Statistic);
            Line("VersionNumber", m.VersionNumber);
            Line("EditTime", m.EditTime);
            Line("Created", Day(m.Dates.CreationDate));
            Line("Modified", Day(m.Dates.ModificationDate));
            Line("Printed", Day(m.Dates.PrintingDate));
            List("User", m.Users);
            List("Email", m.Emails);
            List("Path", m.Paths);
            List("Printer", m.Printers);
            List("Server", m.Servers);
            List("Application", m.Applications);
            List("Password", m.Passwords);
            List("OldVersion", m.OldVersions);
            List("History", m.History);
            if (m.GPS != null)
                Line("GPS", m.GPS.Value);
            foreach (var kv in m.EmbeddedImages.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                sb.Append(indent).Append("EmbeddedImage: ").Append(kv.Key).Append('\n');
                sb.Append(Render(kv.Value, indent + "  "));
            }
            return sb.ToString();
        }
    }
}
