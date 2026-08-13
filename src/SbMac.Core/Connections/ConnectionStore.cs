using System.Text.Json;
using System.Text.Json.Serialization;

namespace SbMac.Core.Connections;

/// <summary>
/// Loads and saves the user's namespace list. Non-secret fields go to a JSON file;
/// connection strings and client secrets go to <see cref="ISecretStore"/>.
/// </summary>
public sealed class ConnectionStore
{
    static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    readonly string filePath;
    readonly ISecretStore secretStore;
    readonly SemaphoreSlim gate = new(1, 1);

    public ConnectionStore(string filePath, ISecretStore secretStore)
    {
        this.filePath = filePath;
        this.secretStore = secretStore;
    }

    /// <summary>Builds a store using the standard config location and the best available secret store.</summary>
    public static ConnectionStore CreateDefault()
    {
        var keychain = new KeychainSecretStore();
        ISecretStore secrets = keychain.IsAvailable
            ? keychain
            : new FileSecretStore(AppPaths.FallbackSecretsFile);

        return new ConnectionStore(AppPaths.ConnectionsFile, secrets);
    }

    /// <summary>
    /// Reads every saved connection and rehydrates its secrets. A connection whose
    /// secret has been removed from the keychain still loads — it just needs the
    /// user to re-enter the value before it can connect.
    /// </summary>
    public async Task<IReadOnlyList<NamespaceConnection>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(filePath))
            {
                return [];
            }

            List<NamespaceConnection>? connections;
            await using (var stream = File.OpenRead(filePath))
            {
                connections = await JsonSerializer
                    .DeserializeAsync<List<NamespaceConnection>>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (connections is null)
            {
                return [];
            }

            foreach (var connection in connections)
            {
                if (connection.AuthenticationMode == AuthenticationMode.ConnectionString)
                {
                    connection.ConnectionString =
                        await secretStore.GetSecretAsync(ConnectionSecretKey(connection), cancellationToken).ConfigureAwait(false);
                }
                else if (connection.EntraCredentialKind == EntraCredentialKind.ClientSecret)
                {
                    connection.ClientSecret =
                        await secretStore.GetSecretAsync(ClientSecretKey(connection), cancellationToken).ConfigureAwait(false);
                }
            }

            return connections;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Writes the full list, splitting secrets out to the secret store.</summary>
    public async Task SaveAsync(IEnumerable<NamespaceConnection> connections, CancellationToken cancellationToken = default)
    {
        var snapshot = connections.ToList();

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var connection in snapshot)
            {
                if (connection.AuthenticationMode == AuthenticationMode.ConnectionString &&
                    !string.IsNullOrWhiteSpace(connection.ConnectionString))
                {
                    await secretStore
                        .SetSecretAsync(ConnectionSecretKey(connection), connection.ConnectionString, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (connection.EntraCredentialKind == EntraCredentialKind.ClientSecret &&
                    !string.IsNullOrWhiteSpace(connection.ClientSecret))
                {
                    await secretStore
                        .SetSecretAsync(ClientSecretKey(connection), connection.ClientSecret, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            // Write to a temp file and move it into place so an interrupted save can't
            // leave a truncated connections.json behind.
            var temporaryPath = filePath + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer
                    .SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Forgets a connection and drops its secrets from the keychain.</summary>
    public async Task DeleteAsync(NamespaceConnection connection, CancellationToken cancellationToken = default)
    {
        var remaining = (await LoadAsync(cancellationToken).ConfigureAwait(false))
            .Where(existing => existing.Id != connection.Id)
            .ToList();

        await secretStore.DeleteSecretAsync(ConnectionSecretKey(connection), cancellationToken).ConfigureAwait(false);
        await secretStore.DeleteSecretAsync(ClientSecretKey(connection), cancellationToken).ConfigureAwait(false);

        await SaveAsync(remaining, cancellationToken).ConfigureAwait(false);
    }

    static string ConnectionSecretKey(NamespaceConnection connection) => $"connection-string:{connection.Id:N}";

    static string ClientSecretKey(NamespaceConnection connection) => $"client-secret:{connection.Id:N}";
}
