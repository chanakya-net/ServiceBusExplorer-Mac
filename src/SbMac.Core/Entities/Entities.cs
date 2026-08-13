using Azure.Messaging.ServiceBus.Administration;

namespace SbMac.Core.Entities;

/// <summary>
/// A queue's definition paired with its live counters. The two come from separate
/// management calls, so runtime may be null when the caller asked for definitions only.
/// </summary>
public sealed record QueueEntity(QueueProperties Properties, QueueRuntimeProperties? Runtime)
{
    public string Name => Properties.Name;

    public long ActiveMessageCount => Runtime?.ActiveMessageCount ?? 0;

    public long DeadLetterMessageCount => Runtime?.DeadLetterMessageCount ?? 0;

    public long ScheduledMessageCount => Runtime?.ScheduledMessageCount ?? 0;

    public long TotalMessageCount => Runtime?.TotalMessageCount ?? 0;

    public long SizeInBytes => Runtime?.SizeInBytes ?? 0;

    public bool IsDisabled => Properties.Status != EntityStatus.Active;
}

/// <summary>A topic's definition paired with its live counters.</summary>
public sealed record TopicEntity(TopicProperties Properties, TopicRuntimeProperties? Runtime)
{
    public string Name => Properties.Name;

    public long ScheduledMessageCount => Runtime?.ScheduledMessageCount ?? 0;

    public long SizeInBytes => Runtime?.SizeInBytes ?? 0;

    public int SubscriptionCount => Runtime?.SubscriptionCount ?? 0;

    public bool IsDisabled => Properties.Status != EntityStatus.Active;
}

/// <summary>A subscription's definition paired with its live counters.</summary>
public sealed record SubscriptionEntity(SubscriptionProperties Properties, SubscriptionRuntimeProperties? Runtime)
{
    public string TopicName => Properties.TopicName;

    public string Name => Properties.SubscriptionName;

    public long ActiveMessageCount => Runtime?.ActiveMessageCount ?? 0;

    public long DeadLetterMessageCount => Runtime?.DeadLetterMessageCount ?? 0;

    public long TotalMessageCount => Runtime?.TotalMessageCount ?? 0;

    public bool IsDisabled => Properties.Status != EntityStatus.Active;
}

/// <summary>
/// Identifies the thing messages are read from or written to. A subscription needs
/// both its topic and its own name, which is why this isn't just a string.
/// </summary>
public sealed record EntityPath
{
    EntityPath(string topicOrQueue, string? subscription)
    {
        TopicOrQueue = topicOrQueue;
        Subscription = subscription;
    }

    public string TopicOrQueue { get; }

    /// <summary>Null for queues.</summary>
    public string? Subscription { get; }

    public bool IsSubscription => Subscription is not null;

    public static EntityPath ForQueue(string name) => new(name, null);

    public static EntityPath ForSubscription(string topicName, string subscriptionName) => new(topicName, subscriptionName);

    /// <summary>The path as Service Bus writes it, e.g. <c>orders/Subscriptions/audit</c>.</summary>
    public override string ToString() =>
        IsSubscription ? $"{TopicOrQueue}/Subscriptions/{Subscription}" : TopicOrQueue;
}
