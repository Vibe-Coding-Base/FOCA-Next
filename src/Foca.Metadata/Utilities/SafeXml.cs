using System.IO;
using System.Xml;

namespace Foca.Metadata.Utilities
{
    /// <summary>
    /// The only way extractors load XML. Documents come from untrusted sources, so DTDs are ignored
    /// (no entity expansion: "billion laughs"), nothing is resolved (no XXE / network access) and the
    /// document size is bounded.
    /// </summary>
    public static class SafeXml
    {
        /// <summary>Upper bound on characters read from one XML part (metadata parts are a few KB).</summary>
        public const long MaxCharacters = 64L * 1024 * 1024;

        public static XmlReaderSettings Settings => new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharacters,
            MaxCharactersFromEntities = 1024,
            IgnoreProcessingInstructions = true,
            CloseInput = false,
        };

        public static XmlDocument Load(Stream stream)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (XmlReader reader = XmlReader.Create(stream, Settings))
                doc.Load(reader);
            return doc;
        }

        public static XmlDocument LoadXml(string xml)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var text = new StringReader(xml))
            using (XmlReader reader = XmlReader.Create(text, Settings))
                doc.Load(reader);
            return doc;
        }

        public static XmlReader CreateReader(Stream stream) => XmlReader.Create(stream, Settings);
    }
}
