using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SbMac.App.Services;

public interface IUpdateCheckStateStore
{
    Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default);
    Task SetLastCheckUtcAsync(DateTimeOffset value, CancellationToken cancellationToken = default);
}

/// <summary>
/// The update checker's timestamp, kept in the shared settings document so it and the
/// user's preferences never overwrite each other.
/// </summary>
public sealed class JsonUpdateCheckStateStore(JsonSettingsStore settings) : IUpdateCheckStateStore
{
    const string LastCheckKey = "lastUpdateCheckUtc";

    public JsonUpdateCheckStateStore(string filePath)
        : this(new JsonSettingsStore(filePath))
    {
    }

    public async Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var root = await settings.ReadAsync(cancellationToken).ConfigureAwait(false);
            var text = root[LastCheckKey]?.GetValue<string>();

            return DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value)
                ? value
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task SetLastCheckUtcAsync(
        DateTimeOffset value,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await settings.UpdateAsync(
                root => root[LastCheckKey] = value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Update state is best-effort and must not affect application startup.
        }
    }
}
