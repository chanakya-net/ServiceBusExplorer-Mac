using System.Diagnostics;
using System.Text;

namespace SbMac.Core.Connections;

/// <summary>
/// Stores connection secrets in the macOS login keychain via the <c>security</c>
/// CLI, so nothing sensitive lands in SB-Mac's JSON config.
/// </summary>
/// <remarks>
/// Shelling out to <c>security</c> avoids a native interop dependency and is the
/// same mechanism the Azure CLI and Docker use for credential storage on macOS.
/// Secrets are passed on stdin rather than argv so they never appear in the
/// process table.
/// </remarks>
public sealed class KeychainSecretStore : ISecretStore
{
    const string ServicePrefix = "SB-Mac";

    public bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists("/usr/bin/security");

    public async Task SetSecretAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        // -U updates in place when an item with the same account+service already exists;
        // without it `security` fails with errSecDuplicateItem (45).
        // -w with no value makes it read the secret from stdin.
        var result = await RunAsync(
            ["add-generic-password", "-a", key, "-s", ServiceName(key), "-U", "-w"],
            stdin: secret,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new SecretStoreException($"Could not save the secret to the macOS keychain: {result.StandardError.Trim()}");
        }
    }

    public async Task<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(
            ["find-generic-password", "-a", key, "-s", ServiceName(key), "-w"],
            stdin: null,
            cancellationToken).ConfigureAwait(false);

        // Exit code 44 is errSecItemNotFound — a normal "nothing saved yet", not a failure.
        if (result.ExitCode != 0)
        {
            return null;
        }

        return result.StandardOutput.TrimEnd('\n', '\r');
    }

    public async Task DeleteSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        // A missing item is the desired end state, so a non-zero exit is not an error here.
        await RunAsync(
            ["delete-generic-password", "-a", key, "-s", ServiceName(key)],
            stdin: null,
            cancellationToken).ConfigureAwait(false);
    }

    static string ServiceName(string key) => $"{ServicePrefix}:{key}";

    static async Task<ProcessResult> RunAsync(string[] arguments, string? stdin, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/security")
        {
            RedirectStandardInput = stdin is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new SecretStoreException("Could not start the macOS `security` tool.");

        // Read both pipes before waiting: `security` can fill a pipe buffer and deadlock otherwise.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask.ConfigureAwait(false),
            await stderrTask.ConfigureAwait(false));
    }

    readonly record struct ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}

public sealed class SecretStoreException : Exception
{
    public SecretStoreException(string message) : base(message) { }
    public SecretStoreException(string message, Exception inner) : base(message, inner) { }
}
