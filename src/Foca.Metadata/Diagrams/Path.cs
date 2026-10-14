using Foca.Metadata.Analysis;
using Foca.Metadata.Utilities;
using System;

namespace Foca.Metadata.Diagrams
{
    [Serializable]
    public class Path : MetadataValue
    {
        public bool IsComputerFolder { get; }

        public Path(string path, bool isComputerFolder) : base(Functions.GetPathFolder(path))
        {
            this.IsComputerFolder = isComputerFolder;
        }

        public override bool IsValid()
        {
            return PathAnalysis.IsValidPath(this.Value.Trim());
        }
    }
}