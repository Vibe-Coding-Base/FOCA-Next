using System;
using System.Collections.Generic;

namespace Foca.Metadata.Diagrams
{
    [Serializable]
    public class Email : MetadataValue
    {
        public Email(string email) : base(email)
        { }
    }
}