using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Foca.Metadata.Utilities
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

    /// <summary>
    /// Read-only wrapper that fails once more than a fixed number of bytes has been read. Used for package
    /// parts opened through System.IO.Packaging, whose inflated size is otherwise unbounded (zip bombs).
    /// </summary>
    public sealed class BoundedStream : Stream
    {
        private readonly Stream inner;
        private readonly long limit;
        private long read;

        public BoundedStream(Stream inner, long limit = SafeZip.DefaultMaxEntryBytes)
        {
            this.inner = inner;
            this.limit = limit;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = inner.Read(buffer, offset, count);
            read += n;
            if (read > limit)
                throw new InvalidDataException($"Package part inflates beyond the {limit} byte limit.");
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
