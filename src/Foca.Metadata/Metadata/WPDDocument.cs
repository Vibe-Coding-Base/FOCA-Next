using Foca.Metadata.Analysis;
using Foca.Metadata.Diagrams;
using Foca.Metadata.Utilities;
using System;
using System.IO;

namespace Foca.Metadata.Extractors
{
    public class WPDDocument : DocumentExtractor
    {
        private enum MetadataType { SharedResource, Printer, File, Unknown };

        public WPDDocument(Stream stm) : base(stm)
        {
        }

        public override FileMetadata AnalyzeFile()
        {
            try
            {
                this.foundMetadata = new FileMetadata();
                if (IsWPD(this.fileStream))
                {
                    // Scan the bytes once; the previous version copied the rest of the file for every
                    // marker found, which was quadratic on crafted input
                    byte[] buffer = this.fileStream.ToArray();
                    int position = 0;
                    int entryPoint;
                    MetadataType tipo;
                    while ((entryPoint = EntryPointString(buffer, position, out tipo)) > -1)
                    {
                        position = entryPoint + 2;

                        var aux = ReadBinaryString16(buffer, position);
                        if (!IsPossibleString(aux)) continue;
                        if (tipo == MetadataType.Unknown && !PathAnalysis.IsValidPath(aux))
                        {
                            if (aux.ToLower().Contains("jet") || aux.ToLower().Contains("printer") || aux.ToLower().Contains("hp") ||
                                aux.ToLower().Contains("series") || aux.ToLower().Contains("canon") || aux.ToLower().Contains("laser") ||
                                aux.ToLower().Contains("epson") || aux.ToLower().Contains("lj") || aux.ToLower().Contains("lexmark") ||
                                aux.ToLower().Contains("xerox") || aux.ToLower().Contains("sharp"))
                            {
                                this.foundMetadata.Add(new Printer(Functions.FilterPrinter(aux)));
                            }
                            else if (aux.ToLower().Contains("acrobat") || aux.ToLower().Contains("adobe") || aux.ToLower().Contains("creator") ||
                                     aux.ToLower().Contains("writer") || aux.ToLower().Contains("pdf") || aux.ToLower().Contains("converter"))
                            {
                                this.foundMetadata.Add(new Application(Functions.FilterPrinter(Analysis.ApplicationAnalysis.GetApplicationsFromString(aux))));
                            }
                        }
                        else
                        {
                            var strPath = Functions.GetPathFolder(aux);
                            if (!PathAnalysis.IsValidPath(strPath)) continue;
                            this.foundMetadata.Add(new Diagrams.Path(PathAnalysis.CleanPath(strPath), true));
                            var strUser = PathAnalysis.ExtractUserFromPath(strPath);
                            if (!string.IsNullOrEmpty(strUser))
                                this.foundMetadata.Add(new User(strUser, true));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.ToString());
            }
            return this.foundMetadata;
        }

        /// <summary>Offset of the next string marker at or after <paramref name="start"/>, or -1.</summary>
        private static int EntryPointString(byte[] buffer, int start, out MetadataType tipo)
        {
            tipo = MetadataType.Unknown;
            for (var i = start; i < buffer.Length - 2; i++)
            {
                if (buffer[i] == 0x00 && buffer[i + 1] == 0x98)
                    return i;

                if (buffer.Length < i + 7) continue;
                if (buffer[i] == 0x00 &&
                    buffer[i + 1] == 0x01 &&
                    buffer[i + 2] == 0x00 &&
                    buffer[i + 3] > 0x00 &&
                    buffer[i + 4] == 0x00 &&
                    buffer[i + 5] == 0x01 &&
                    buffer[i + 6] == 0x00)
                {
                    tipo = MetadataType.File;
                    return i + 5;
                }
            }
            return -1;
        }

        /// <summary>Longest UTF-16 string read: paths and printer names are far shorter.</summary>
        private const int MaxStringChars = 1024;

        /// <summary>Reads a NUL-terminated little-endian UTF-16 string starting at <paramref name="offset"/>.</summary>
        private static string ReadBinaryString16(byte[] buffer, int offset)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = offset; i + 1 < buffer.Length && sb.Length < MaxStringChars; i += 2)
            {
                short value = (short)(buffer[i] | (buffer[i + 1] << 8));
                if (value <= 0)
                    break;
                sb.Append((char)value);
            }
            return sb.ToString();
        }

        private static bool IsPossibleString(string str)
        {
            if (str == null)
                return false;
            if (str.Contains("?"))
                return false;
            if (str.Length == 1)
                return false;

            var chars = "abcdefghijklmnñopqrstuvwxyz0123456789:\\ ";

            return str.IndexOfAny(chars.ToCharArray()) != -1;
        }

        /// <summary>
        /// Check for the WPD header
        /// </summary>
        /// <param name="fs">Stream que contiene un archivo pwd</param>
        /// <returns>true si el archivo es wpd</returns>
        private static bool IsWPD(Stream fs)
        {
            var aux = fs.Position;
            var cabecera = new byte[4];
            fs.Seek(0, SeekOrigin.Begin);
            if (fs.Length < 4)
                return false;
            fs.Read(cabecera, 0, 4);
            //0xFF,'W','P','C'
            if ((cabecera[0] == 0xff) &
                (cabecera[1] == 0x57) &
                (cabecera[2] == 0x50) &
                (cabecera[3] == 0x43))
            {
                fs.Seek(aux, SeekOrigin.Begin);
                return true;
            }
            fs.Seek(aux, SeekOrigin.Begin);
            return false;
        }
    }
}
