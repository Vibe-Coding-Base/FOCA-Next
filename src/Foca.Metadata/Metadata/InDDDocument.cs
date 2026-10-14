using Foca.Metadata.Analysis;
using Foca.Metadata.Diagrams;
using Foca.Metadata.Utilities;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Foca.Metadata.Extractors
{
    public class InDDDocument : XMPExtractor
    {
        // The whole untrusted file is scanned: bound every pattern (nested quantifiers, ReDoS)
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Compiled;
        private static readonly Regex PathRegex = new Regex(@"@([a-z]:|\\)\\(([a-z0-9\s\-_\$&()ñÇ/n/r]+)\\)*[a-z0-9\s,;.\-_\$%&()=ñ{}Ç/n/r+@]+", Options, RegexTimeout);
        private static readonly Regex PrinterRegex = new Regex(@"winspool\0([a-z]:|\\)\\(([a-z0-9\s\-_\$&()ñÇ/n/r]+)\\)*[a-z0-9\s,;.\-_\$%&()=ñ{}Ç/n/r+@]+", Options, RegexTimeout);
        // Lazy and NUL-free: XMP packets are text, and greedy matching across a large file is quadratic
        private static readonly Regex XmpMetaRegex = new Regex(@"<x:xmpmeta[^\0]*?</x:xmpmeta>", Options, RegexTimeout);
        private static readonly Regex RdfRegex = new Regex(@"<rdf:RDF[^\0]*?</rdf:RDF>", Options, RegexTimeout);

        public InDDDocument(Stream stm) : base(stm)
        {
        }

        /// <summary>
        /// Extract metadata.
        /// </summary>
        public override FileMetadata AnalyzeFile()
        {
            try
            {
                this.foundMetadata = new FileMetadata();
                using (StreamReader sr = new StreamReader(this.fileStream))
                {
                    String sRead = sr.ReadToEnd();
                    foreach (Match m in PathRegex.Matches(sRead))
                    {
                        String path = m.Value.Trim();
                        path = path.Substring(1);
                        this.foundMetadata.Add(new Diagrams.Path(PathAnalysis.CleanPath(path), true));
                    }
                    foreach (Match m in PrinterRegex.Matches(sRead))
                    {
                        String printer = m.Value.Trim();
                        printer = printer.Substring(9);
                        this.foundMetadata.Add(new Printer(Functions.FilterPrinter(printer)));
                    }
                    foreach (Match m in XmpMetaRegex.Matches(sRead))
                    {
                        String xmp = m.Value.Trim();
                        ReadXMPMetadata(xmp);
                    }
                    foreach (Match m in RdfRegex.Matches(sRead))
                    {
                        String xmp = m.Value.Trim();
                        ReadXMPMetadata(xmp);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
            return this.foundMetadata;
        }

        /// <summary>
        /// Search the XMP metadata
        /// </summary>
        /// <param name="doc">A open PdfDocument</param>
        public void ReadXMPMetadata(string xmp)
        {
            if (xmp != string.Empty)
            {
                System.Xml.XmlDocument xDoc = SafeXml.LoadXml(xmp);
                this.ExtractFromXMP(xDoc);
            }
        }
    }
}
