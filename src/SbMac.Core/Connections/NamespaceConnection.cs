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
    /// The namespace host this connection ultimately talks to, regardless of auth mode.
    /// Used for display and for keying the keychain entry.
    /// </summary>
    [JsonIgnore]
    public string ResolvedNamespace =>
        AuthenticationMode == AuthenticationMode.EntraId
            ? FullyQualifiedNamespace ?? string.Empty
            : ConnectionStringParser.TryGetEndpointHost(ConnectionString) ?? string.Empty;

    public NamespaceConnection Clone() => (NamespaceConnection)MemberwiseClone();
}

/// <summary>Mirrors <c>Azure.Messaging.ServiceBus.ServiceBusTransportType</c> without leaking the SDK type into persisted JSON.</summary>
public enum ServiceBusTransport
{
    AmqpTcp,
    AmqpWebSockets
}
