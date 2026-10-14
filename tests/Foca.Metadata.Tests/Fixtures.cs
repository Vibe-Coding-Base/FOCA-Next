using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Runtime.Serialization;
using System.Text;
using PdfSharp.Pdf;

namespace Foca.Metadata.Tests
{
    /// <summary>
    /// Builds deterministic sample documents in memory so the test corpus needs no binary files.
    /// Every fixture carries a known set of "leaky" metadata (users, paths, printers, servers, software).
    /// </summary>
    internal static class Fixtures
    {
        // Midday timestamps: snapshots render dates only, so writers that shift by the local UTC offset
        // (PdfSharp does) still produce the same calendar day on every build machine.
        public static readonly DateTime Created = new DateTime(2020, 1, 2, 12, 0, 0, DateTimeKind.Unspecified);
        public static readonly DateTime Modified = new DateTime(2021, 6, 7, 12, 0, 0, DateTimeKind.Unspecified);

        public static byte[] Jpeg()
        {
            using (var bmp = new Bitmap(8, 8))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.Clear(Color.CornflowerBlue);

                SetAscii(bmp, 0x010F, "Canon");                    // Make
                SetAscii(bmp, 0x0110, "Canon EOS 5D");             // Model
                SetAscii(bmp, 0x0131, "Adobe Photoshop CS6 (Windows)"); // Software
                SetAscii(bmp, 0x013B, "exif.photographer");        // Artist

                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Jpeg);
                    return ms.ToArray();
                }
            }
        }

        private static void SetAscii(Image img, int id, string value)
        {
            var item = (PropertyItem)FormatterServices.GetUninitializedObject(typeof(PropertyItem));
            byte[] bytes = Encoding.ASCII.GetBytes(value + "\0");
            item.Id = id;
            item.Type = 2;
            item.Len = bytes.Length;
            item.Value = bytes;
            img.SetPropertyItem(item);
        }

        public static byte[] Docx()
        {
            const string coreXml =
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<cp:coreProperties xmlns:cp=""http://schemas.openxmlformats.org/package/2006/metadata/core-properties"" xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:dcterms=""http://purl.org/dc/terms/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
  <dc:title>Quarterly Report</dc:title>
  <dc:creator>alice.author</dc:creator>
  <cp:lastModifiedBy>bob.editor</cp:lastModifiedBy>
  <cp:revision>7</cp:revision>
  <dcterms:created xsi:type=""dcterms:W3CDTF"">2020-01-02T03:04:05Z</dcterms:created>
  <dcterms:modified xsi:type=""dcterms:W3CDTF"">2021-06-07T08:09:10Z</dcterms:modified>
</cp:coreProperties>";
            const string appXml =
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Properties xmlns=""http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"">
  <Template>Normal.dotm</Template>
  <TotalTime>42</TotalTime>
  <Application>Microsoft Office Word</Application>
  <AppVersion>16.0000</AppVersion>
  <Company>ACME Corp</Company>
  <Manager>carol.manager</Manager>
</Properties>";
            const string documentXml =
@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:document xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main"">
  <w:body>
    <w:p><w:ins w:id=""1"" w:author=""dave.reviewer""><w:r><w:t>added</w:t></w:r></w:ins></w:p>
    <w:p><w:del w:id=""2"" w:author=""dave.reviewer""><w:r><w:delText>removed</w:delText></w:r></w:del></w:p>
  </w:body>
</w:document>";

            using (var ms = new MemoryStream())
            {
                using (Package pkg = Package.Open(ms, FileMode.Create))
                {
                    pkg.PackageProperties.Creator = "alice.author";
                    pkg.PackageProperties.LastModifiedBy = "bob.editor";
                    pkg.PackageProperties.Title = "Quarterly Report";
                    pkg.PackageProperties.Created = Created;
                    pkg.PackageProperties.Modified = Modified;

                    AddPart(pkg, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml", coreXml);
                    AddPart(pkg, "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml", appXml);
                    PackagePart doc = AddPart(pkg, "/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml", documentXml);
                    doc.CreateRelationship(new Uri("http://intranet.acme.local/share/plan.docx"), TargetMode.External,
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink");
                    doc.CreateRelationship(new Uri("file:///\\\\fileserver01\\docs\\budget.xlsx"), TargetMode.External,
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink");

                    PackagePart img = pkg.CreatePart(new Uri("/word/media/image1.jpeg", UriKind.Relative), "image/jpeg");
                    byte[] jpeg = Jpeg();
                    using (Stream s = img.GetStream())
                        s.Write(jpeg, 0, jpeg.Length);
                }
                return ms.ToArray();
            }
        }

        private static PackagePart AddPart(Package pkg, string uri, string contentType, string xml)
        {
            PackagePart part = pkg.CreatePart(new Uri(uri, UriKind.Relative), contentType);
            using (var w = new StreamWriter(part.GetStream(), new UTF8Encoding(false)))
                w.Write(xml);
            return part;
        }

        public static byte[] Odt()
        {
            const string metaXml =
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-meta xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:meta=""urn:oasis:names:tc:opendocument:xmlns:meta:1.0"" xmlns:dc=""http://purl.org/dc/elements/1.1/"" office:version=""1.2"">
  <office:meta>
    <meta:generator>LibreOffice/7.3.7.2$Linux_X86_64 LibreOffice_project/30$Build-2</meta:generator>
    <meta:initial-creator>erin.creator</meta:initial-creator>
    <dc:creator>frank.modifier</dc:creator>
    <meta:printed-by>grace.printer</meta:printed-by>
    <meta:creation-date>2020-01-02T03:04:05</meta:creation-date>
    <dc:date>2021-06-07T08:09:10</dc:date>
    <dc:title>Network Inventory</dc:title>
    <dc:language>es-ES</dc:language>
    <meta:template xmlns:xlink=""http://www.w3.org/1999/xlink"" xlink:href=""file:///C:/Users/erin.creator/Templates/corp.ott""/>
  </office:meta>
</office:document-meta>";
            const string contentXml =
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-content xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:text=""urn:oasis:names:tc:opendocument:xmlns:text:1.0"" xmlns:xlink=""http://www.w3.org/1999/xlink"">
  <office:body><office:text>
    <text:p><text:a xlink:href=""mailto:it-support@acme.local?subject=help"">mail</text:a></text:p>
    <text:p><text:a xlink:href=""file:///C:/Users/frank.modifier/Desktop/passwords.xlsx"">file</text:a></text:p>
    <text:p><text:a xlink:href=""http://192.168.10.20/intranet"">ip</text:a></text:p>
  </office:text></office:body>
</office:document-content>";
            const string settingsXml =
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<office:document-settings xmlns:office=""urn:oasis:names:tc:opendocument:xmlns:office:1.0"" xmlns:config=""urn:oasis:names:tc:opendocument:xmlns:config:1.0"">
  <office:settings><config:config-item-set config:name=""ooo:configuration-settings"">
    <config:config-item config:name=""PrinterName"" config:type=""string"">\\PRINTSRV01\HP-LaserJet-4F</config:config-item>
  </config:config-item-set></office:settings>
</office:document-settings>";

            using (var ms = new MemoryStream())
            {
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    AddEntry(zip, "mimetype", Encoding.ASCII.GetBytes("application/vnd.oasis.opendocument.text"), CompressionLevel.NoCompression);
                    AddEntry(zip, "meta.xml", Encoding.UTF8.GetBytes(metaXml));
                    AddEntry(zip, "content.xml", Encoding.UTF8.GetBytes(contentXml));
                    AddEntry(zip, "settings.xml", Encoding.UTF8.GetBytes(settingsXml));
                    AddEntry(zip, "Pictures/photo.jpg", Jpeg());
                }
                return ms.ToArray();
            }
        }

        private static void AddEntry(ZipArchive zip, string name, byte[] data, CompressionLevel level = CompressionLevel.Optimal)
        {
            ZipArchiveEntry e = zip.CreateEntry(name, level);
            using (Stream s = e.Open())
                s.Write(data, 0, data.Length);
        }

        public static byte[] Pdf()
        {
            using (var doc = new PdfDocument())
            {
                doc.Info.Author = "henry.pdfauthor";
                doc.Info.Creator = "Microsoft® Word 2016";
                doc.Info.Title = "Confidential Memo";
                doc.Info.Subject = "Budget";
                doc.Info.Keywords = "internal";
                doc.Info.CreationDate = Created;
                doc.Info.ModificationDate = Modified;
                doc.AddPage();
                using (var ms = new MemoryStream())
                {
                    doc.Save(ms, false);
                    return ms.ToArray();
                }
            }
        }

        public static byte[] Svg() => Encoding.UTF8.GetBytes(
@"<?xml version=""1.0"" encoding=""UTF-8""?>
<svg xmlns=""http://www.w3.org/2000/svg"" xmlns:xlink=""http://www.w3.org/1999/xlink""
     xmlns:sodipodi=""http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd""
     xmlns:inkscape=""http://www.inkscape.org/namespaces/inkscape""
     sodipodi:docname=""logo.svg""
     inkscape:export-filename=""C:\Users\ivan.designer\Desktop\logo.png""
     width=""10"" height=""10"">
  <image xlink:href=""file:///C:/Users/ivan.designer/Pictures/background.png"" width=""10"" height=""10""/>
</svg>");

        /// <summary>
        /// A structurally valid compound file (MS-CFB v3) whose directory sector chain loops 1 -> 2 -> 1.
        /// </summary>
        public static byte[] CyclicOle()
        {
            const int sector = 512;
            var data = new byte[sector * 4];
            using (var w = new BinaryWriter(new MemoryStream(data)))
            {
                w.Write(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });
                w.Write(new byte[16]);              // CLSID
                w.Write((ushort)0x003E);            // minor version
                w.Write((ushort)3);                 // major version
                w.Write((ushort)0xFFFE);            // byte order
                w.Write((ushort)9);                 // sector shift: 512
                w.Write((ushort)6);                 // mini sector shift: 64
                w.Write(new byte[10]);              // reserved + directory sector count
                w.Write(1);                         // FAT sectors
                w.Write(1);                         // first directory sector
                w.Write(0);                         // transaction signature
                w.Write(4096);                      // mini stream cutoff
                w.Write(-2);                        // first mini FAT sector (none)
                w.Write(0);                         // mini FAT sectors
                w.Write(-2);                        // first DIFAT sector (none)
                w.Write(0);                         // DIFAT sectors
                w.Write(0);                         // DIFAT[0]: FAT lives in sector 0
                for (int i = 1; i < 109; i++)
                    w.Write(-1);

                w.Seek(sector, SeekOrigin.Begin);   // sector 0: FAT
                w.Write(-3);                        // sector 0 is a FAT sector
                w.Write(2);                         // directory: 1 -> 2
                w.Write(1);                         //            2 -> 1 (cycle)
                for (int i = 3; i < sector / 4; i++)
                    w.Write(-1);
            }
            return data;
        }

        public static byte[] Rdp() => Encoding.UTF8.GetBytes(string.Join("\r\n",
            "full address:s:ts01.acme.local",
            "gatewayhostname:s:rdgw.acme.local:443",
            "username:s:ACME\\judy.admin",
            "alternate shell:s:C:\\Program Files\\Microsoft Office\\Office16\\EXCEL.EXE",
            "shell working directory:s:C:\\Users\\judy.admin\\Documents",
            "screen mode id:i:2",
            ""));
    }
}
