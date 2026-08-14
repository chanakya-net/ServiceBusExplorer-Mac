using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SbMac.App.Services;

public interface IUpdateCheckStateStore
{
    Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default);
    Task SetLastCheckUtcAsync(DateTimeOffset value, CancellationToken cancellationToken = default);
}

public sealed class JsonUpdateCheckStateStore(string filePath) : IUpdateCheckStateStore
{
    public async Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var root = JsonNode.Parse(await File.ReadAllTextAsync(filePath, cancellationToken)) as JsonObject;
            var text = root?["lastUpdateCheckUtc"]?.GetValue<string>();

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
            JsonObject root = new();
            if (File.Exists(filePath))
            {
                try
                {
                    root = JsonNode.Parse(await File.ReadAllTextAsync(filePath, cancellationToken)) as JsonObject
                        ?? new JsonObject();
                }
                catch (JsonException)
                {
                    // Invalid settings contain no preservable properties; replace them.
                }
            }

            root["lastUpdateCheckUtc"] = value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(
                filePath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Update state is best-effort and must not affect application startup.
        }
    }
}
