using System;

namespace Foca.Metadata.Diagrams
{
    [Serializable]
    public class Printer : MetadataValue
    {
        public Printer(string printerName) : base(printerName)
        {
        }
    }
}