using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SbMac.App.ViewModels.Tree;

/// <summary>
/// Base for everything in the left-hand tree. Nodes carry their own display state so
/// the tree can be bound directly without a parallel model.
/// </summary>
public abstract partial class TreeNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    string title = string.Empty;

    /// <summary>
    /// Name of the icon to draw, resolved against Styles/Icons.axaml. A name rather than a
    /// geometry keeps drawing types out of the view models.
    /// </summary>
    [ObservableProperty]
    string icon = string.Empty;

    /// <summary>Trailing text shown in a muted colour — subscription counts, auth mode, and such.</summary>
    [ObservableProperty]
    string? detail;

    /// <summary>Active message count, formatted for a badge. Null hides the badge.</summary>
    [ObservableProperty]
    string? activeBadge;

    /// <summary>Dead-letter count, formatted for a badge. Null hides the badge.</summary>
    [ObservableProperty]
    string? deadLetterBadge;

    [ObservableProperty]
    bool isExpanded;

    [ObservableProperty]
    bool isSelected;

    /// <summary>True while the node is loading its children.</summary>
    [ObservableProperty]
    bool isBusy;

    /// <summary>Dimmed rendering for disabled entities.</summary>
    [ObservableProperty]
    bool isDisabled;

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public TreeNodeViewModel? Parent { get; init; }

    /// <summary>The namespace this node belongs to, found by walking up the tree.</summary>
    public NamespaceNodeViewModel? Namespace
    {
        get
        {
            for (var node = this; node is not null; node = node.Parent)
            {
                if (node is NamespaceNodeViewModel namespaceNode)
                {
                    return namespaceNode;
                }
            }

            return null;
        }
    }

    /// <summary>Reloads this node's children from the service. The base implementation does nothing.</summary>
    public virtual Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
