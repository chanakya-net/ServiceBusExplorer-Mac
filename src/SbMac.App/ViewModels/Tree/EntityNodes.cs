using SbMac.Core.Entities;

namespace SbMac.App.ViewModels.Tree;

/// <summary>The "Queues" folder under a namespace.</summary>
public sealed class QueueFolderNodeViewModel : TreeNodeViewModel
{
    public QueueFolderNodeViewModel()
    {
        Title = "Queues";
        Icon = "IconFolder";
        IsExpanded = true;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var entities = Namespace?.Entities;
        if (entities is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var queues = await entities.GetQueuesAsync(cancellationToken).ConfigureAwait(true);

            Children.Clear();
            foreach (var queue in queues)
            {
                Children.Add(new QueueNodeViewModel(queue) { Parent = this });
            }

            Detail = queues.Count == 0 ? "empty" : queues.Count.ToString("N0");
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>The "Topics" folder under a namespace.</summary>
public sealed class TopicFolderNodeViewModel : TreeNodeViewModel
{
    public TopicFolderNodeViewModel()
    {
        Title = "Topics";
        Icon = "IconFolder";
        IsExpanded = true;
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var entities = Namespace?.Entities;
        if (entities is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var topics = await entities.GetTopicsAsync(cancellationToken).ConfigureAwait(true);

            // The topics go into the tree before their subscriptions are fetched, so the
            // list is browsable while the counts are still filling in.
            var nodes = topics
                .Select(topic => new TopicNodeViewModel(topic) { Parent = this })
                .ToList();

            Children.Clear();
            foreach (var node in nodes)
            {
                Children.Add(node);
            }

            Detail = topics.Count == 0 ? "empty" : topics.Count.ToString("N0");

            // Subscriptions are loaded eagerly: their counts are the reason most people
            // open a topic. Loading them together rather than one topic at a time is what
            // keeps a namespace with many topics from crawling.
            await RefreshAllAsync(nodes, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>A queue, with its active and dead-letter counts.</summary>
public sealed class QueueNodeViewModel : TreeNodeViewModel
{
    public QueueNodeViewModel(QueueEntity entity)
    {
        Icon = "IconQueue";
        Apply(entity);
    }

    public QueueEntity Entity { get; private set; } = null!;

    public EntityPath Path => EntityPath.ForQueue(Entity.Name);

    protected override bool IsSearchCandidate => true;

    public void Apply(QueueEntity entity)
    {
        Entity = entity;
        Title = entity.Name;
        IsDisabled = entity.IsDisabled;

        ActiveBadge = FormatCount(entity.ActiveMessageCount);
        DeadLetterBadge = FormatCount(entity.DeadLetterMessageCount);
        Detail = entity.ScheduledMessageCount > 0 ? $"{entity.ScheduledMessageCount:N0} scheduled" : null;

        OnPropertyChanged(nameof(Entity));
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var entities = Namespace?.Entities;
        if (entities is null)
        {
            return;
        }

        Apply(await entities.GetQueueAsync(Entity.Name, cancellationToken).ConfigureAwait(true));
    }

    /// <summary>Zero is left off entirely — a badge reading "0" is noise on every row.</summary>
    internal static string? FormatCount(long count) => count > 0 ? count.ToString("N0") : null;
}

/// <summary>A topic; its children are its subscriptions.</summary>
public sealed class TopicNodeViewModel : TreeNodeViewModel
{
    public TopicNodeViewModel(TopicEntity entity)
    {
        Icon = "IconTopic";
        Apply(entity);
    }

    public TopicEntity Entity { get; private set; } = null!;

    protected override bool IsSearchCandidate => true;

    protected override bool ShowDescendantsWhenSearchMatches => true;

    public void Apply(TopicEntity entity)
    {
        Entity = entity;
        Title = entity.Name;
        IsDisabled = entity.IsDisabled;
        OnPropertyChanged(nameof(Entity));
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var entities = Namespace?.Entities;
        if (entities is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Apply(await entities.GetTopicAsync(Entity.Name, cancellationToken).ConfigureAwait(true));

            var subscriptions = await entities.GetSubscriptionsAsync(Entity.Name, cancellationToken).ConfigureAwait(true);

            Children.Clear();
            foreach (var subscription in subscriptions)
            {
                Children.Add(new SubscriptionNodeViewModel(subscription) { Parent = this });
            }

            Detail = subscriptions.Count switch
            {
                0 => "no subscriptions",
                1 => "1 subscription",
                var count => $"{count} subscriptions"
            };
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>A subscription on a topic.</summary>
public sealed class SubscriptionNodeViewModel : TreeNodeViewModel
{
    public SubscriptionNodeViewModel(SubscriptionEntity entity)
    {
        Icon = "IconSubscription";
        Apply(entity);
    }

    public SubscriptionEntity Entity { get; private set; } = null!;

    public EntityPath Path => EntityPath.ForSubscription(Entity.TopicName, Entity.Name);

    protected override bool IsSearchCandidate => true;

    public void Apply(SubscriptionEntity entity)
    {
        Entity = entity;
        Title = entity.Name;
        IsDisabled = entity.IsDisabled;

        ActiveBadge = QueueNodeViewModel.FormatCount(entity.ActiveMessageCount);
        DeadLetterBadge = QueueNodeViewModel.FormatCount(entity.DeadLetterMessageCount);

        OnPropertyChanged(nameof(Entity));
    }

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var entities = Namespace?.Entities;
        if (entities is null)
        {
            return;
        }

        Apply(await entities
            .GetSubscriptionAsync(Entity.TopicName, Entity.Name, cancellationToken)
            .ConfigureAwait(true));
    }
}
