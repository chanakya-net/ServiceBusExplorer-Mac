using System.Text.Json;

namespace SbMac.Core.Connections;

/// <summary>
/// Fallback secret store used when the macOS keychain isn't reachable (for
/// example when SB-Mac runs on Linux in CI). Secrets sit in a 0600 file under the
/// app's config directory.
/// </summary>
/// <remarks>
/// This is deliberately weaker than the keychain: the file is readable by anything
/// running as the user. <see cref="KeychainSecretStore"/> is preferred whenever it
/// reports itself available.
/// </remarks>
public sealed class FileSecretStore : ISecretStore
{
    readonly string filePath;
    readonly SemaphoreSlim gate = new(1, 1);

    public FileSecretStore(string filePath)
    {
        this.filePath = filePath;
    }

    public bool IsAvailable => true;

    public async Task SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var secrets = await ReadAsync(cancellationToken).ConfigureAwait(false);
            secrets[key] = secret;
            await WriteAsync(secrets, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var secrets = await ReadAsync(cancellationToken).ConfigureAwait(false);
            return secrets.GetValueOrDefault(key);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var secrets = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (secrets.Remove(key))
            {
                await WriteAsync(secrets, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    async Task<Dictionary<string, string>> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        await using var stream = File.OpenRead(filePath);
        var secrets = await JsonSerializer
            .DeserializeAsync<Dictionary<string, string>>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return secrets ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    async Task WriteAsync(Dictionary<string, string> secrets, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        await using (var stream = File.Create(filePath))
        {
            await JsonSerializer
                .SerializeAsync(stream, secrets, new JsonSerializerOptions { WriteIndented = true }, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
