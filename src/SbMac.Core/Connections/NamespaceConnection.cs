using System.Text.Json.Serialization;

namespace SbMac.Core.Connections;

/// <summary>
/// A saved Service Bus namespace the user can connect to. This is the unit that
/// gets persisted to disk by <see cref="ConnectionStore"/>.
/// </summary>
public sealed class NamespaceConnection
{
    /// <summary>Stable identity for the entry, so renames don't orphan it.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display name shown in the namespace tree.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether this namespace is browsed as Service Bus or as Event Hubs. Defaults to
    /// Service Bus so connections saved before Event Hubs support existed keep working.
    /// </summary>
    public NamespaceKind Kind { get; set; } = NamespaceKind.ServiceBus;

    public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.ConnectionString;

    /// <summary>
    /// SAS connection string. Only meaningful when <see cref="AuthenticationMode"/>
    /// is <see cref="AuthenticationMode.ConnectionString"/>. Never persisted in plain
    /// text — <see cref="ConnectionStore"/> moves it to the macOS keychain.
    /// </summary>
    [JsonIgnore]
    public string? ConnectionString { get; set; }

    /// <summary>e.g. <c>contoso.servicebus.windows.net</c>. Required for Entra ID auth.</summary>
    public string? FullyQualifiedNamespace { get; set; }

    public EntraCredentialKind EntraCredentialKind { get; set; } = EntraCredentialKind.Default;

    /// <summary>Entra tenant to authenticate against. Optional; falls back to the credential's default.</summary>
    public string? TenantId { get; set; }

    /// <summary>App registration client id. Only used by <see cref="EntraCredentialKind.ClientSecret"/> and interactive flows.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Client secret for <see cref="EntraCredentialKind.ClientSecret"/>. Kept out of the
    /// JSON file for the same reason as <see cref="ConnectionString"/>.
    /// </summary>
    [JsonIgnore]
    public string? ClientSecret { get; set; }

    /// <summary>Transport to use. AMQP over WebSockets gets through proxies that block port 5671.</summary>
    public ServiceBusTransport Transport { get; set; } = ServiceBusTransport.AmqpTcp;

    /// <summary>
    /// Event hubs to show under this namespace when they can't be enumerated.
    /// </summary>
    /// <remarks>
    /// Only meaningful when <see cref="Kind"/> is <see cref="NamespaceKind.EventHubs"/>.
    /// The Event Hubs data-plane SDK can describe a hub you name but cannot list the hubs
    /// in a namespace — that is a management-plane operation. So the list is discovered
    /// through ARM when the identity can read it, and typed in here when it can't.
    /// </remarks>
    public List<string> EventHubNames { get; set; } = [];

    /// <summary>
    /// Consumer group to read events through. Blank means <c>$Default</c>. Reading from a
    /// group an application is actively using is safe — Event Hubs reads never remove data.
    /// </summary>
    public string? ConsumerGroup { get; set; }

    /// <summary>
    /// Azure subscription to enumerate event hubs from. Optional: when blank, ARM
    /// discovery uses the credential's default subscription.
    /// </summary>
    public string? SubscriptionId { get; set; }

    /// <summary>
    /// The namespace host this connection ultimately talks to, regardless of auth mode.
    /// Used for display and for keying the keychain entry.
    /// </summary>
    [JsonIgnore]
    public string ResolvedNamespace =>
        AuthenticationMode == AuthenticationMode.EntraId
            ? FullyQualifiedNamespace ?? string.Empty
            : ConnectionStringParser.TryGetEndpointHost(ConnectionString) ?? string.Empty;

    /// <summary>The consumer group to read through, with the service default applied.</summary>
    [JsonIgnore]
    public string EffectiveConsumerGroup =>
        string.IsNullOrWhiteSpace(ConsumerGroup) ? DefaultConsumerGroup : ConsumerGroup.Trim();

    /// <summary>The consumer group every event hub has and no-one can delete.</summary>
    public const string DefaultConsumerGroup = "$Default";

    /// <summary>
    /// Every event hub name this connection knows about without asking Azure: the ones
    /// the user listed, plus the one an <c>EntityPath=</c> token pins the connection to.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> ConfiguredEventHubNames
    {
        get
        {
            var names = new List<string>();

            void Add(string? name)
            {
                var trimmed = name?.Trim();
                if (!string.IsNullOrEmpty(trimmed) &&
                    !names.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(trimmed);
                }
            }

            // An entity-scoped connection string can only reach the hub it names, so that
            // hub belongs in the list whether or not the user also typed it.
            Add(ConnectionStringParser.TryGetValue(ConnectionString, "EntityPath"));

            foreach (var name in EventHubNames)
            {
                Add(name);
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }

    public NamespaceConnection Clone()
    {
        var clone = (NamespaceConnection)MemberwiseClone();

        // MemberwiseClone shares the list instance, which would make edits to a copy
        // reach back into the original.
        clone.EventHubNames = [.. EventHubNames];
        return clone;
    }
}

/// <summary>Mirrors <c>Azure.Messaging.ServiceBus.ServiceBusTransportType</c> without leaking the SDK type into persisted JSON.</summary>
public enum ServiceBusTransport
{
    AmqpTcp,
    AmqpWebSockets
}
