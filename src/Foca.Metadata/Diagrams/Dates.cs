using System;

namespace Foca.Metadata.Diagrams
{
    [Serializable]
    public struct Dates
    {
        public DateTime? CreationDate { get; set; }

        public DateTime? ModificationDate { get; set; }

        public DateTime? PrintingDate { get; set; }
    }
}
