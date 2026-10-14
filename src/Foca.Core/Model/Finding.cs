namespace Foca.Core.Model;

/// <summary>The kinds of leaked information FOCA extracts, matching the "Metadata summary" of the WinForms UI.</summary>
public enum FindingKind
{
    User,
    Email,
    Path,
    Printer,
    Software,
    Server,
    Password,
    OperatingSystem,
    Company,
    Template,
    GeoLocation,
    Link,
}

/// <summary>One piece of leaked information found in a document.</summary>
public class Finding
{
    public int Id { get; set; }

    public int DocumentId { get; set; }

    public FindingKind Kind { get; set; }

    /// <summary>Value as found in the document.</summary>
    public string Value { get; set; } = "";

    /// <summary>
    /// Canonical form used to group variants of the same value across documents
    /// (e.g. "ACME\jdoe", "jdoe@acme.com" and "J.Doe" all normalise to related keys).
    /// </summary>
    public string NormalizedValue { get; set; } = "";

    /// <summary>Where in the document it was found (metadata field, embedded image, old version...).</summary>
    public string? Context { get; set; }
}
