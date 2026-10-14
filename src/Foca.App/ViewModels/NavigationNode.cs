using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Foca.Core.Model;

namespace Foca.App.ViewModels;

public enum NodeKind
{
    Project,
    Network,
    Domains,
    DocumentAnalysis,
    Files,
    MetadataSummary,
    SummaryKind,
}

/// <summary>A node of the left tree, laid out like the original FOCA: Project > Network, Domains, Document Analysis.</summary>
public sealed partial class NavigationNode : ObservableObject
{
    public NodeKind Kind { get; }

    /// <summary>For <see cref="NodeKind.SummaryKind"/> nodes: the kind of finding listed.</summary>
    public FindingKind? FindingKind { get; }

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private bool isExpanded = true;

    public ObservableCollection<NavigationNode> Children { get; } = new();

    public NavigationNode(NodeKind kind, string title, FindingKind? findingKind = null)
    {
        Kind = kind;
        this.title = title;
        FindingKind = findingKind;
    }

    /// <summary>The original FOCA icon for this node.</summary>
    public Bitmap? Icon => AppIcons.Get(Kind switch
    {
        NodeKind.Project => "project",
        NodeKind.Network => "network",
        NodeKind.Domains => "domains",
        NodeKind.DocumentAnalysis => "documents",
        NodeKind.Files => "documents",
        NodeKind.MetadataSummary => "metadata",
        NodeKind.SummaryKind when FindingKind.HasValue => AppIcons.NameFor(FindingKind.Value),
        _ => "metadata",
    });
}
