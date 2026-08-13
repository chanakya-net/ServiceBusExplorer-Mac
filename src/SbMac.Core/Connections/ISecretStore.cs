namespace SbMac.Core.Connections;

/// <summary>
/// Somewhere to keep connection secrets that isn't the config file.
/// </summary>
public interface ISecretStore
{
    /// <summary>False when the backing store can't be used on this machine; callers should fall back.</summary>
    bool IsAvailable { get; }

    Task SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default);

    Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default);
}
