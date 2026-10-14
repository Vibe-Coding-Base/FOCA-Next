using Foca.Metadata.Utilities;
using Xunit;

namespace Foca.Metadata.Tests
{
    /// <summary>
    /// Document metadata contains Windows paths. These must be parsed the same way on every OS, so the
    /// extractors must not use System.IO.Path (which ignores '\' on Linux/macOS). This regression was
    /// caught by CI: odt/rdp/svg snapshots differed on Linux before Functions.GetFileName was introduced.
    /// </summary>
    public class PathHandlingTests
    {
        [Theory]
        [InlineData(@"C:\Users\ivan.designer\Desktop\logo.png", "logo.png")]
        [InlineData(@"C:\Program Files\Microsoft Office\Office16\EXCEL.EXE", "EXCEL.EXE")]
        [InlineData("/home/user/file.odt", "file.odt")]
        [InlineData("Pictures/photo.jpg", "photo.jpg")]
        [InlineData(@"C:\Users\x\Desktop\", "")]
        [InlineData("noseparator", "noseparator")]
        public void GetFileNameTreatsBothSeparators(string path, string expected)
        {
            Assert.Equal(expected, Functions.GetFileName(path));
        }

        [Theory]
        [InlineData(@"C:\Users\ivan.designer\Desktop\logo.png", @"C:\Users\ivan.designer\Desktop\")]
        [InlineData(@"C:\Users\judy.admin\Documents", @"C:\Users\judy.admin\")]
        [InlineData("/home/erin/templates/corp.ott", "/home/erin/templates/")]
        public void GetPathFolderRemovesTheFileNameOnAnyOS(string path, string expected)
        {
            Assert.Equal(expected, Functions.GetPathFolder(path));
        }
    }
}
