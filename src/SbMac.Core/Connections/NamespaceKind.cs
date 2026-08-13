namespace SbMac.Core.Connections;

/// <summary>
/// Which Azure messaging service a saved namespace points at. Both live on the same
/// <c>*.servicebus.windows.net</c> host, so the host alone can't tell them apart —
/// the user picks this when adding the namespace.
/// </summary>
public enum NamespaceKind
{
    /// <summary>Queues, topics, subscriptions and rules.</summary>
    ServiceBus,

    /// <summary>Event hubs, partitions and consumer groups.</summary>
    EventHubs
}
