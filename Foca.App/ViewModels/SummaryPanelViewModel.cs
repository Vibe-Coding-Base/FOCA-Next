using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Foca.Core.Model;
using Foca.Core.Reporting;

namespace Foca.App.ViewModels;

public sealed class SummaryRow
{
    public SummaryRow(SummaryEntry entry)
    {
        Entry = entry;
    }

    public SummaryEntry Entry { get; }
    public string Kind => Entry.Kind.ToString();
    public string Value => Entry.Value;
    public int Documents => Entry.DocumentCount;
    public string Variants => Entry.Variants.Count > 1 ? string.Join(", ", Entry.Variants) : "";
    public string FoundIn => string.Join(Environment.NewLine, Entry.Documents.Select(Path.GetFileName));
}

/// <summary>"Metadata summary" of the project, optionally filtered to one kind (Users, Printers...).</summary>
public sealed partial class SummaryPanelViewModel : ObservableObject
{
    public ObservableCollection<SummaryRow> Rows { get; } = new();

    [ObservableProperty]
    private string title = "Metadata summary";

    [ObservableProperty]
    private string filter = "";

    private IReadOnlyList<SummaryEntry> all = Array.Empty<SummaryEntry>();
    private FindingKind? kind;

    public void Show(IReadOnlyList<SummaryEntry> entries, FindingKind? onlyKind)
    {
        all = entries;
        kind = onlyKind;
        Title = onlyKind == null ? "Metadata summary" : $"{onlyKind} ({entries.Count(e => e.Kind == onlyKind)})";
        Apply();
    }

    partial void OnFilterChanged(string value) => Apply();

    private void Apply()
    {
        Rows.Clear();
        foreach (SummaryEntry e in all.Where(e => (kind == null || e.Kind == kind) &&
                                                 (Filter.Length == 0 || e.Value.Contains(Filter, StringComparison.OrdinalIgnoreCase))))
            Rows.Add(new SummaryRow(e));
    }
}
