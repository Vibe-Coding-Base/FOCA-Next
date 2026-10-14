using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace MetadataExtractCore.Utilities
{
    /// <summary>
    /// Read-only access to ZIP based documents (OpenDocument, OOXML) downloaded from untrusted sources.
    /// Entries are only ever inflated into memory, and every read is capped so a crafted archive
    /// (zip bomb) cannot exhaust memory.
    /// </summary>
    public sealed class SafeZip : IDisposable
    {
        public const long DefaultMaxEntryBytes = 64L * 1024 * 1024;
        public const int MaxEntries = 10000;

        private readonly ZipArchive archive;
        private readonly long maxEntryBytes;

        private SafeZip(ZipArchive archive, long maxEntryBytes)
        {
            this.archive = archive;
            this.maxEntryBytes = maxEntryBytes;
        }

        /// <summary>Opens the archive without taking ownership of <paramref name="stream"/>.</summary>
        /// <exception cref="InvalidDataException">The stream is not a valid ZIP archive or has too many entries.</exception>
        public static SafeZip Open(Stream stream, long maxEntryBytes = DefaultMaxEntryBytes)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            stream.Seek(0, SeekOrigin.Begin);
            var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaxEntries)
            {
                archive.Dispose();
                throw new InvalidDataException($"The archive has more than {MaxEntries} entries.");
            }
            return new SafeZip(archive, maxEntryBytes);
        }

        public IEnumerable<string> EntryNames => archive.Entries.Select(e => e.FullName);

        public bool Contains(string entryName) => archive.GetEntry(entryName) != null;

        /// <summary>Inflates an entry into a new MemoryStream positioned at the start.</summary>
        /// <exception cref="InvalidDataException">The entry inflates beyond the configured limit.</exception>
        public MemoryStream ReadEntry(string entryName)
        {
            ZipArchiveEntry entry = archive.GetEntry(entryName)
                ?? throw new FileNotFoundException("Entry not found in archive", entryName);

            if (entry.Length > maxEntryBytes)
                throw new InvalidDataException($"Entry '{entryName}' declares {entry.Length} bytes, above the {maxEntryBytes} byte limit.");

            var output = new MemoryStream();
            using (Stream input = entry.Open())
            {
                // The declared size can lie, so the limit is enforced on the bytes actually inflated.
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > maxEntryBytes)
                    {
                        output.Dispose();
                        throw new InvalidDataException($"Entry '{entryName}' inflates beyond the {maxEntryBytes} byte limit.");
                    }
                    output.Write(buffer, 0, read);
                }
            }
            output.Seek(0, SeekOrigin.Begin);
            return output;
        }

        public void Dispose()
        {
            archive.Dispose();
        }
    }
}
