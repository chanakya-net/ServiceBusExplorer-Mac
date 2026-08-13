namespace SbMac.Core.EventHubs;

/// <summary>
/// An event hub as the tree shows it: its identity plus the partitions and consumer
/// groups it was found to have.
/// </summary>
/// <param name="ConsumerGroups">
/// Empty when the groups could not be listed — that needs management-plane access, which
/// a data-plane SAS key does not grant. It never means the hub has no consumer groups,
/// because <c>$Default</c> always exists.
/// </param>
/// <param name="Error">
/// Why this hub could not be described, when it couldn't. A hub the user named but that
/// does not exist stays in the tree carrying its error rather than vanishing.
/// </param>
public sealed record EventHubEntity(
    string Name,
    DateTimeOffset CreatedOn,
    IReadOnlyList<string> PartitionIds,
    IReadOnlyList<string> ConsumerGroups,
    string? Error = null)
{
    public int PartitionCount => PartitionIds.Count;

    public bool IsUnreachable => Error is not null;

    /// <summary>A hub that is named but not yet described — no partitions, no error.</summary>
    public static EventHubEntity Unreachable(string name, string error) =>
        new(name, default, [], [], error);
}

/// <summary>
/// One partition's live cursor. Event Hubs is an append-only log, so these bounds are
/// what tells you where to start reading and how much is retained.
/// </summary>
public sealed record PartitionEntity(
    string EventHubName,
    string Id,
    long BeginningSequenceNumber,
    long LastEnqueuedSequenceNumber,
    DateTimeOffset LastEnqueuedTime,
    bool IsEmpty)
{
    /// <summary>
    /// How many events are currently retained. Retention expiry moves the beginning
    /// sequence number forward, so this shrinks over time without anything being consumed.
    /// </summary>
    public long RetainedEventCount =>
        IsEmpty ? 0 : LastEnqueuedSequenceNumber - BeginningSequenceNumber + 1;
}
