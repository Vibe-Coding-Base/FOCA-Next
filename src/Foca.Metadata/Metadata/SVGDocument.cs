using System;
using System.IO;
using System.Xml;
using System.Text.RegularExpressions;
using Foca.Metadata.Analysis;
using Foca.Metadata.Utilities;
using Foca.Metadata.Diagrams;

namespace Foca.Metadata.Extractors
{
    public class SVGDocument : DocumentExtractor
    {
        public SVGDocument(Stream stm):base(stm)
        {
        }

        /// <summary>Largest inflated .svgz accepted (gzip bombs).</summary>
        private const long MaxInflatedBytes = 64L * 1024 * 1024;

        public override FileMetadata AnalyzeFile()
        {
            XmlReader avgReader = null;
            Stream content = null;

            try
            {
                this.foundMetadata = new FileMetadata();
                content = OpenContent(this.fileStream);
                avgReader = SafeXml.CreateReader(content);
                avgReader.Read();

                while (avgReader.Read())
                {
                    // node's value, example: <a>/home/user/file</a>
                    if (CheckPath(avgReader.Value))
                    {
                        var cleanPath = PathAnalysis.CleanPath(avgReader.Value);
                        var user = PathAnalysis.ExtractUserFromPath(cleanPath);
                        if (user != string.Empty)
                            this.foundMetadata.Add(new User(user, true));
                        this.foundMetadata.Add(new Diagrams.Path(cleanPath, true));
                    }

                    while (avgReader.MoveToNextAttribute())
                    {
                        // attribute's value, example: <a atrib="/home/user/file"/>
                        if (!CheckPath(avgReader.Value)) continue;
                        var cleanPath = PathAnalysis.CleanPath(avgReader.Value);
                        var user = PathAnalysis.ExtractUserFromPath(cleanPath);
                        if (user != string.Empty)
                            this.foundMetadata.Add(new User(user, true));
                        this.foundMetadata.Add(new Diagrams.Path(cleanPath, true));
                    }

                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("error: " + ex.Message);
            }
            finally
            {
                avgReader?.Close();
                if (content != null && content != this.fileStream)
                    content.Dispose();
            }
            return this.foundMetadata;
        }

        /// <summary>.svgz files are gzip-compressed SVG: inflate them, bounded, before parsing.</summary>
        private static Stream OpenContent(MemoryStream file)
        {
            file.Seek(0, SeekOrigin.Begin);
            if (file.Length < 2 || file.ReadByte() != 0x1F || file.ReadByte() != 0x8B)
            {
                file.Seek(0, SeekOrigin.Begin);
                return file;
            }
            file.Seek(0, SeekOrigin.Begin);
            var inflated = new MemoryStream();
            using (var gzip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (inflated.Length + read > MaxInflatedBytes)
                        throw new InvalidDataException("Compressed SVG inflates beyond the size limit.");
                    inflated.Write(buffer, 0, read);
                }
            }
            inflated.Seek(0, SeekOrigin.Begin);
            return inflated;
        }

        private static bool CheckPath(string path)
        {
            return Regex.Matches(path, @"^([a-z]:|\\\\|/)", RegexOptions.IgnoreCase).Count > 0;
        }
    }
}
