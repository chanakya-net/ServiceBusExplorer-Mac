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

    /// <summary>
    /// Refreshes a set of nodes at once, bounded so a large namespace doesn't open hundreds
    /// of requests in one go. Loading them one after another is what made a namespace with
    /// many topics take so long to appear.
    /// </summary>
    /// <remarks>
    /// Every task is started from — and awaited back onto — the calling thread, which is
    /// the UI thread. That matters: the nodes mutate observable collections the tree is
    /// bound to, and those must not be touched from the thread pool.
    /// </remarks>
    protected static async Task RefreshAllAsync(
        IReadOnlyList<TreeNodeViewModel> nodes,
        CancellationToken cancellationToken,
        int maxConcurrency = 6)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));

        var pending = new List<Task>(nodes.Count);
        foreach (var node in nodes)
        {
            pending.Add(RefreshOneAsync(node));
        }

        await Task.WhenAll(pending).ConfigureAwait(true);

        async Task RefreshOneAsync(TreeNodeViewModel node)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(true);
            try
            {
                await node.RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
