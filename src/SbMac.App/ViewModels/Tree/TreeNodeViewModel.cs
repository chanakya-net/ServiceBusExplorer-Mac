using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SbMac.App.ViewModels.Tree;

/// <summary>
/// Base for everything in the left-hand tree. Nodes carry their own display state so
/// the tree can be bound directly without a parallel model.
/// </summary>
public abstract partial class TreeNodeViewModel : ViewModelBase
{
    string currentSearchText = string.Empty;
    bool isVisibleBecauseAncestorMatches;
    bool? expansionBeforeSearch;

    protected TreeNodeViewModel()
    {
        Children.CollectionChanged += (_, eventArgs) =>
        {
            if (eventArgs.NewItems is not null)
            {
                var showAllChildren = isVisibleBecauseAncestorMatches ||
                    (IsSearchCandidate && ShowDescendantsWhenSearchMatches &&
                     EntityNameMatcher.IsMatch(Title, currentSearchText));

                foreach (TreeNodeViewModel child in eventArgs.NewItems)
                {
                    child.ApplySearchCore(currentSearchText, showAllChildren);
                }
            }

            RecalculateSearchVisibility();
        };
    }

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

    /// <summary>False hides the tree item while the entity search is active.</summary>
    [ObservableProperty]
    bool isVisible = true;

    public ObservableCollection<TreeNodeViewModel> Children { get; } = [];

    public TreeNodeViewModel? Parent { get; init; }

    /// <summary>Only Service Bus entities participate in the sidebar entity search.</summary>
    protected virtual bool IsSearchCandidate => false;

    /// <summary>A topic match keeps its subscriptions visible, even when their names differ.</summary>
    protected virtual bool ShowDescendantsWhenSearchMatches => false;

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

    /// <summary>Applies an entity-name search to this node and every descendant.</summary>
    internal void ApplySearch(string? searchText) =>
        ApplySearchCore(EntityNameMatcher.Normalize(searchText), ancestorMatches: false);

    void ApplySearchCore(string normalizedSearchText, bool ancestorMatches)
    {
        var searchWasActive = currentSearchText.Length > 0;
        var searchIsActive = normalizedSearchText.Length > 0;
        if (!searchWasActive && searchIsActive)
        {
            expansionBeforeSearch = IsExpanded;
        }

        currentSearchText = normalizedSearchText;
        isVisibleBecauseAncestorMatches = ancestorMatches;

        if (!searchIsActive)
        {
            IsVisible = true;
            foreach (var child in Children)
            {
                child.ApplySearchCore(normalizedSearchText, ancestorMatches: false);
            }

            if (searchWasActive && expansionBeforeSearch is { } previousExpansion)
            {
                IsExpanded = previousExpansion;
            }

            expansionBeforeSearch = null;

            return;
        }

        var selfMatches = IsSearchCandidate && EntityNameMatcher.IsMatch(Title, normalizedSearchText);
        var showAllChildren = ancestorMatches || (selfMatches && ShowDescendantsWhenSearchMatches);

        foreach (var child in Children)
        {
            child.ApplySearchCore(normalizedSearchText, showAllChildren);
        }

        IsVisible = ancestorMatches || selfMatches || Children.Any(child => child.IsVisible);
        if (Children.Any(child => child.IsVisible))
        {
            IsExpanded = true;
        }
    }

    void RecalculateSearchVisibility()
    {
        IsVisible = currentSearchText.Length == 0 ||
            isVisibleBecauseAncestorMatches ||
            (IsSearchCandidate && EntityNameMatcher.IsMatch(Title, currentSearchText)) ||
            Children.Any(child => child.IsVisible);

        if (currentSearchText.Length > 0 && Children.Any(child => child.IsVisible))
        {
            IsExpanded = true;
        }

        Parent?.RecalculateSearchVisibility();
    }

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
