using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

using SbMac.Core.Connections;

namespace SbMac.Core;

/// <summary>
/// A live connection to one Service Bus namespace: the management client used to
/// browse and edit entities, and the messaging client used to send and receive.
/// </summary>
/// <remarks>
/// Both SDK clients are expensive to create and safe to share, so a session is
/// created once per namespace and reused for the lifetime of the connection.
/// </remarks>
public sealed class ServiceBusSession : IAsyncDisposable
{
    ServiceBusSession(
        NamespaceConnection connection,
        ServiceBusAdministrationClient administration,
        ServiceBusClient messaging)
    {
        Connection = connection;
        Administration = administration;
        Messaging = messaging;
    }

    public NamespaceConnection Connection { get; }

    /// <summary>Entity CRUD and runtime counters.</summary>
    public ServiceBusAdministrationClient Administration { get; }

    /// <summary>Senders and receivers.</summary>
    public ServiceBusClient Messaging { get; }

    /// <summary>The namespace host, e.g. <c>contoso.servicebus.windows.net</c>.</summary>
    public string Namespace => Messaging.FullyQualifiedNamespace;

    /// <summary>
    /// Opens a session for <paramref name="connection"/>. This only builds the clients;
    /// no network call happens until the first operation, so call
    /// <see cref="TestConnectionAsync"/> if you want to fail fast.
    /// </summary>
    public static ServiceBusSession Create(NamespaceConnection connection)
    {
        var transport = connection.Transport == ServiceBusTransport.AmqpWebSockets
            ? ServiceBusTransportType.AmqpWebSockets
            : ServiceBusTransportType.AmqpTcp;

        var clientOptions = new ServiceBusClientOptions { TransportType = transport };

        if (connection.AuthenticationMode == AuthenticationMode.EntraId)
        {
            var fullyQualifiedNamespace = connection.FullyQualifiedNamespace?.Trim();
            if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
            {
                throw new InvalidOperationException(
                    "Entra ID authentication needs a fully qualified namespace, e.g. contoso.servicebus.windows.net.");
            }

            var credential = CredentialFactory.Create(connection);

            return new ServiceBusSession(
                connection,
                new ServiceBusAdministrationClient(fullyQualifiedNamespace, credential),
                new ServiceBusClient(fullyQualifiedNamespace, credential, clientOptions));
        }

        var connectionString = connection.ConnectionString?.Trim();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("This connection has no connection string. Re-enter it in the connection dialog.");
        }

        return new ServiceBusSession(
            connection,
            new ServiceBusAdministrationClient(connectionString),
            new ServiceBusClient(connectionString, clientOptions));
    }

    /// <summary>
    /// Makes one cheap management call so connection and permission problems surface
    /// immediately rather than on the first entity click.
    /// </summary>
    public async Task TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        // Enumerating one page of queues exercises auth, DNS, and the management
        // endpoint without assuming any particular entity exists.
        await foreach (var _ in Administration.GetQueuesAsync(cancellationToken).ConfigureAwait(false))
        {
            break;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Messaging.DisposeAsync().ConfigureAwait(false);
    }
}
