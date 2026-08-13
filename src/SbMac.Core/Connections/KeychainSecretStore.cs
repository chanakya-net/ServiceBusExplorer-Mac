namespace SbMac.Core.Connections;

/// <summary>
/// Stores connection secrets in the macOS login keychain, so nothing sensitive lands in
/// SB-Mac's JSON config.
/// </summary>
/// <remarks>
/// Backed by the Security framework rather than the <c>security</c> command-line tool —
/// see <see cref="MacKeychain"/> for why the CLI cannot be used for this.
/// </remarks>
public sealed class KeychainSecretStore : ISecretStore
{
    /// <summary>
    /// Keychain items are grouped under this service name, so everything SB-Mac stores is
    /// findable (and removable) as one group in Keychain Access.
    /// </summary>
    const string ServiceName = "SB-Mac";

    public bool IsAvailable => OperatingSystem.IsMacOS();

    public Task SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MacKeychain.Set(ServiceName, key, secret);
        return Task.CompletedTask;
    }

    public Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MacKeychain.Get(ServiceName, key));
    }

    public Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MacKeychain.Delete(ServiceName, key);
        return Task.CompletedTask;
    }
}

public sealed class SecretStoreException : Exception
{
    public SecretStoreException(string message) : base(message) { }
    public SecretStoreException(string message, Exception inner) : base(message, inner) { }
}
