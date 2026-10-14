using System.Collections.ObjectModel;
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

    public string Icon => Kind switch
    {
        NodeKind.Project => "🌐",
        NodeKind.Network => "🖧",
        NodeKind.Domains => "🔗",
        NodeKind.DocumentAnalysis => "📂",
        NodeKind.Files => "📄",
        NodeKind.MetadataSummary => "🗂",
        _ => "•",
    };
}
