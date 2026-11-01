using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SbMac.App.Services;

/// <summary>
/// The settings document shared by every consumer of <c>settings.json</c>. Each update
/// reads the current file, applies one change and atomically replaces the file while
/// holding a process-wide gate for that path, so concurrent writers never drop each
/// other's keys and readers never observe a partially written document.
/// </summary>
public sealed class JsonSettingsStore
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    readonly SemaphoreSlim gate;
    readonly Func<CancellationToken, Task>? beforePublish;

    public JsonSettingsStore(string filePath)
    {
        // Relative paths and dot-segment aliases of the same file share one gate.
        FilePath = Path.GetFullPath(filePath);
        gate = Gates.GetOrAdd(FilePath, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>Test seam: runs after the replacement is fully written, before it is published.</summary>
    internal JsonSettingsStore(string filePath, Func<CancellationToken, Task> beforePublish)
        : this(filePath) => this.beforePublish = beforePublish;

    public string FilePath { get; }

    /// <summary>
    /// Reads the current document. A missing, malformed or non-object file reads as an
    /// empty document; I/O and permission failures are thrown to the caller.
    /// </summary>
    public JsonObject Read() => File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : [];

    /// <inheritdoc cref="Read" />
    public async Task<JsonObject> ReadAsync(CancellationToken cancellationToken = default) =>
        File.Exists(FilePath)
            ? Parse(await File.ReadAllTextAsync(FilePath, cancellationToken).ConfigureAwait(false))
            : [];

    /// <summary>
    /// Applies <paramref name="mutate"/> to a fresh read of the document and atomically
    /// replaces the file. Unrelated and unknown keys are carried through unchanged. If the
    /// existing file cannot be read, or the change fails or is cancelled, the previous
    /// document is left exactly as it was.
    /// </summary>
    public async Task UpdateAsync(Action<JsonObject> mutate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var root = await ReadAsync(cancellationToken).ConfigureAwait(false);
            mutate(root);
            await ReplaceAsync(root.ToJsonString(WriteOptions), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    async Task ReplaceAsync(string json, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(FilePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await stream.WriteAsync(Encoding.UTF8.GetBytes(json), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (beforePublish is not null)
            {
                await beforePublish(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    static JsonObject Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            // Invalid settings contain no preservable properties.
            return [];
        }
    }
}
