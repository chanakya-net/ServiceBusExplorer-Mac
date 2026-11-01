using System.Text.Json.Nodes;

using SbMac.App.Models;

namespace SbMac.App.Services;

/// <summary>
/// The user's saved preferences. Changes take effect in memory — and are announced —
/// immediately; persistence follows in the background, one save at a time and in the
/// order the changes were made, so the last choice is always the one left on disk.
/// </summary>
public sealed class AppPreferencesService(JsonSettingsStore store)
{
    const string ThemeKey = "theme";
    const string SchemaVersionKey = "schemaVersion";

    readonly Lock saveLock = new();
    Task<bool> pendingSave = Task.FromResult(true);

    public AppPreferences Current { get; private set; } = AppPreferences.Default;

    /// <summary>Raised on the caller's thread as soon as a preference changes.</summary>
    public event EventHandler<AppPreferences>? Changed;

    /// <summary>Raised when a queued save completes, on its completion thread.</summary>
    public event EventHandler? SaveCompleted;

    /// <summary>The failure from the most recent save, or null when it succeeded.</summary>
    public string? LastSaveError { get; private set; }

    public bool HasPendingSaves
    {
        get
        {
            lock (saveLock)
            {
                return !pendingSave.IsCompleted;
            }
        }
    }

    /// <summary>
    /// Reads the saved preferences without modifying the file. Anything missing,
    /// unrecognised or unreadable falls back to its default.
    /// </summary>
    public AppPreferences Load()
    {
        JsonObject root;
        try
        {
            root = store.Read();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            root = [];
        }

        Current = new AppPreferences(ParseTheme(root[ThemeKey]));
        return Current;
    }

    /// <summary>
    /// Switches the theme now and queues it for saving. The returned task reports whether
    /// this particular save reached disk; the in-memory choice stands either way.
    /// </summary>
    public Task<bool> SetThemeAsync(AppTheme theme)
    {
        theme = Enum.IsDefined(theme) ? theme : AppTheme.System;
        var updated = Current with { Theme = theme };
        Current = updated;
        Changed?.Invoke(this, updated);

        lock (saveLock)
        {
            pendingSave = SaveAfterAsync(pendingSave, theme);
            return pendingSave;
        }
    }

    /// <summary>Waits for every queued save. Returns whether the last one succeeded.</summary>
    public Task<bool> FlushAsync()
    {
        lock (saveLock)
        {
            return pendingSave;
        }
    }

    async Task<bool> SaveAfterAsync(Task<bool> previous, AppTheme theme)
    {
        await previous.ConfigureAwait(false);
        bool saved;
        try
        {
            await store.UpdateAsync(root => WriteTheme(root, theme)).ConfigureAwait(false);
            LastSaveError = null;
            saved = true;
        }
        catch (Exception exception)
        {
            // The choice already applies for this session.
            LastSaveError = exception.Message;
            saved = false;
        }

        SaveCompleted?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    static void WriteTheme(JsonObject root, AppTheme theme)
    {
        // A newer schema written by a later version is kept, so a downgrade does not
        // misdescribe fields it does not understand.
        if (root[SchemaVersionKey] is not JsonValue version ||
            !version.TryGetValue<int>(out var existing) ||
            existing < AppPreferences.SchemaVersion)
        {
            root[SchemaVersionKey] = AppPreferences.SchemaVersion;
        }

        root[ThemeKey] = theme switch
        {
            AppTheme.Light => "light",
            AppTheme.Dark => "dark",
            _ => "system"
        };
    }

    static AppTheme ParseTheme(JsonNode? node)
    {
        var text = node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
        return text switch
        {
            "light" => AppTheme.Light,
            "dark" => AppTheme.Dark,
            _ => AppTheme.System
        };
    }
}
