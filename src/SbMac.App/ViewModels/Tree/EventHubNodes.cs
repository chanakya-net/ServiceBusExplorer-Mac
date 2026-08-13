using SbMac.Core.EventHubs;

namespace SbMac.App.ViewModels.Tree;

/// <summary>The "Event Hubs" folder under an Event Hubs namespace.</summary>
public sealed class EventHubFolderNodeViewModel : TreeNodeViewModel
{
    public EventHubFolderNodeViewModel()
    {
        Title = "Event Hubs";
        Icon = "IconFolder";
        IsExpanded = true;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var service = Namespace?.EventHubs;
        if (service is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var hubs = await service.GetEventHubsAsync(cancellationToken).ConfigureAwait(true);

            Children.Clear();
            foreach (var hub in hubs)
            {
                var node = new EventHubNodeViewModel(hub) { Parent = this };
                Children.Add(node);

                // Partition cursors are the reason to open a hub at all, and a namespace
                // holds few enough hubs that loading them up front stays quick.
                await node.RefreshAsync(cancellationToken).ConfigureAwait(true);
            }

            Detail = hubs.Count switch
            {
                0 => "none configured",
                var count => count.ToString("N0")
            };
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>An event hub; its children are its partitions.</summary>
public sealed class EventHubNodeViewModel : TreeNodeViewModel
{
    public EventHubNodeViewModel(EventHubEntity entity)
    {
        Icon = "IconEventHub";
        Apply(entity);
    }

    public EventHubEntity Entity { get; private set; } = null!;

    /// <summary>
    /// Total events currently retained across every partition. Derived from the loaded
    /// partition nodes rather than cached, so it can never disagree with the tree below it.
    /// </summary>
    public long RetainedEventCount => Children
        .OfType<PartitionNodeViewModel>()
        .Sum(partition => partition.Entity.RetainedEventCount);

    public void Apply(EventHubEntity entity)
    {
        Entity = entity;
        Title = entity.Name;

        // An unreachable hub is dimmed like a disabled entity rather than hidden, so the
        // name the user typed stays visible next to the reason it didn't work.
        IsDisabled = entity.IsUnreachable;
        Detail = entity.IsUnreachable
            ? "unreachable"
            : entity.PartitionCount switch
            {
                0 => "no partitions",
                1 => "1 partition",
                var count => $"{count} partitions"
            };

        OnPropertyChanged(nameof(Entity));
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var service = Namespace?.EventHubs;
        if (service is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Apply(await service
                .GetEventHubAsync(Entity.Name, Entity.ConsumerGroups, cancellationToken)
                .ConfigureAwait(true));

            if (Entity.IsUnreachable)
            {
                Children.Clear();
                ActiveBadge = null;
                return;
            }

            var partitions = await service.GetPartitionsAsync(Entity.Name, cancellationToken).ConfigureAwait(true);

            Children.Clear();
            foreach (var partition in partitions)
            {
                Children.Add(new PartitionNodeViewModel(partition) { Parent = this });
            }

            ActiveBadge = QueueNodeViewModel.FormatCount(RetainedEventCount);
            OnPropertyChanged(nameof(RetainedEventCount));
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>One partition of an event hub.</summary>
public sealed class PartitionNodeViewModel : TreeNodeViewModel
{
    public PartitionNodeViewModel(PartitionEntity entity)
    {
        Icon = "IconPartition";
        Apply(entity);
    }

    public PartitionEntity Entity { get; private set; } = null!;

    public string EventHubName => Entity.EventHubName;

    public string PartitionId => Entity.Id;

    public void Apply(PartitionEntity entity)
    {
        Entity = entity;
        Title = $"Partition {entity.Id}";

        ActiveBadge = QueueNodeViewModel.FormatCount(entity.RetainedEventCount);
        Detail = entity.IsEmpty ? "empty" : $"last {entity.LastEnqueuedTime.ToLocalTime():MMM d, HH:mm}";

        OnPropertyChanged(nameof(Entity));
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var service = Namespace?.EventHubs;
        if (service is null)
        {
            return;
        }

        Apply(await service
            .GetPartitionAsync(Entity.EventHubName, Entity.Id, cancellationToken)
            .ConfigureAwait(true));
    }
}
