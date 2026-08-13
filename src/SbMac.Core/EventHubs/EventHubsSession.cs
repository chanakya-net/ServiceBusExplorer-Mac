using Azure.Core;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;

using SbMac.Core.Connections;

namespace SbMac.Core.EventHubs;

/// <summary>
/// A live connection to one Event Hubs namespace.
/// </summary>
/// <remarks>
/// Unlike Service Bus, whose clients are namespace-scoped, every Event Hubs client is
/// bound to a single hub. A session therefore holds one client per hub and hands them
/// out on demand, because they are expensive to create and safe to share.
/// </remarks>
public sealed class EventHubsSession : IAsyncDisposable
{
    readonly Dictionary<string, EventHubProducerClient> producers = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, EventHubConsumerClient> consumers = new(StringComparer.OrdinalIgnoreCase);
    readonly Lock gate = new();

    readonly string? connectionString;
    readonly string? entityPath;
    readonly string? fullyQualifiedNamespace;
    readonly TokenCredential? credential;
    readonly EventHubsTransportType transport;

    bool disposed;

    EventHubsSession(
        NamespaceConnection connection,
        string? connectionString,
        string? entityPath,
        string? fullyQualifiedNamespace,
        TokenCredential? credential)
    {
        Connection = connection;
        this.connectionString = connectionString;
        this.entityPath = entityPath;
        this.fullyQualifiedNamespace = fullyQualifiedNamespace;
        this.credential = credential;

        transport = connection.Transport == ServiceBusTransport.AmqpWebSockets
            ? EventHubsTransportType.AmqpWebSockets
            : EventHubsTransportType.AmqpTcp;
    }

    public NamespaceConnection Connection { get; }

    /// <summary>The namespace host, e.g. <c>contoso.servicebus.windows.net</c>.</summary>
    public string Namespace => Connection.ResolvedNamespace;

    /// <summary>The consumer group every read goes through.</summary>
    public string ConsumerGroup => Connection.EffectiveConsumerGroup;

    /// <summary>
    /// The single hub this session is pinned to, when the connection string carries an
    /// <c>EntityPath=</c> token. Null when the credentials reach the whole namespace.
    /// </summary>
    public string? PinnedEventHub => entityPath;

    /// <summary>
    /// Builds the clients' shared configuration. No network call happens here, so call
    /// <see cref="TestConnectionAsync"/> if you want to fail fast.
    /// </summary>
    public static EventHubsSession Create(NamespaceConnection connection)
    {
        if (connection.AuthenticationMode == AuthenticationMode.EntraId)
        {
            var host = connection.FullyQualifiedNamespace?.Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                throw new InvalidOperationException(
                    "Entra ID authentication needs a fully qualified namespace, e.g. contoso.servicebus.windows.net.");
            }

            return new EventHubsSession(connection, null, null, host, CredentialFactory.Create(connection));
        }

        var value = connection.ConnectionString?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                "This connection has no connection string. Re-enter it in the connection dialog.");
        }

        var pinned = ConnectionStringParser.TryGetValue(value, "EntityPath");
        return new EventHubsSession(
            connection,
            value,
            string.IsNullOrWhiteSpace(pinned) ? null : pinned.Trim(),
            null,
            null);
    }

    /// <summary>Returns the cached producer for <paramref name="eventHubName"/>, creating it on first use.</summary>
    public EventHubProducerClient Producer(string eventHubName)
    {
        var name = Validate(eventHubName);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (producers.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var options = new EventHubProducerClientOptions
            {
                ConnectionOptions = new EventHubConnectionOptions { TransportType = transport }
            };

            var client = credential is not null
                ? new EventHubProducerClient(fullyQualifiedNamespace!, name, credential, options)
                : entityPath is not null
                    // A connection string with EntityPath is already bound to its hub, and
                    // passing the name again is rejected as a conflicting configuration.
                    ? new EventHubProducerClient(connectionString!, options)
                    : new EventHubProducerClient(connectionString!, name, options);

            producers[name] = client;
            return client;
        }
    }

    /// <summary>Returns the cached consumer for <paramref name="eventHubName"/>, creating it on first use.</summary>
    public EventHubConsumerClient Consumer(string eventHubName)
    {
        var name = Validate(eventHubName);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            if (consumers.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var options = new EventHubConsumerClientOptions
            {
                ConnectionOptions = new EventHubConnectionOptions { TransportType = transport }
            };

            var client = credential is not null
                ? new EventHubConsumerClient(ConsumerGroup, fullyQualifiedNamespace!, name, credential, options)
                : entityPath is not null
                    ? new EventHubConsumerClient(ConsumerGroup, connectionString!, options)
                    : new EventHubConsumerClient(ConsumerGroup, connectionString!, name, options);

            consumers[name] = client;
            return client;
        }
    }

    /// <summary>
    /// Makes one cheap data-plane call so credential and network problems surface at
    /// connect time. Does nothing when no hub is known yet — there is nothing namespace
    /// wide to call, so discovery is what will report the problem instead.
    /// </summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var probe = PinnedEventHub ?? Connection.ConfiguredEventHubNames.FirstOrDefault();
        if (probe is null)
        {
            return;
        }

        await Consumer(probe).GetEventHubPropertiesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        List<EventHubProducerClient> openProducers;
        List<EventHubConsumerClient> openConsumers;

        lock (gate)
        {
            disposed = true;
            openProducers = [.. producers.Values];
            openConsumers = [.. consumers.Values];
            producers.Clear();
            consumers.Clear();
        }

        foreach (var producer in openProducers)
        {
            await producer.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var consumer in openConsumers)
        {
            await consumer.DisposeAsync().ConfigureAwait(false);
        }
    }

    string Validate(string eventHubName)
    {
        var name = eventHubName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("An event hub name is required.", nameof(eventHubName));
        }

        if (entityPath is not null && !entityPath.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"This connection string is scoped to the event hub “{entityPath}” and cannot reach “{name}”. " +
                "Use a namespace-level connection string to browse more than one hub.");
        }

        return name;
    }
}
