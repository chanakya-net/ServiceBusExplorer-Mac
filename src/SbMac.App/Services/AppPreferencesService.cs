using System.Text.Json.Nodes;

using SbMac.App.Models;

namespace SbMac.App.Services;

/// <summary>
/// The user's saved preferences. Changes take effect in memory — and are announced —
/// immediately; persistence follows in the background, one save at a time and in the
/// order the changes were made, so the last choice is always the one left on disk.
/// </summary>
public sealed class AppPreferencesService
{
    const string ThemeKey = "theme";
    const string SchemaVersionKey = "schemaVersion";

    readonly JsonSettingsStore store;
    readonly Func<TimeSpan, CancellationToken, Task> delay;
    CancellationTokenSource? workspaceDelay;
    WorkspacePreferences? delayedWorkspace;

    public AppPreferencesService(JsonSettingsStore store) : this(store, Task.Delay) { }

    internal AppPreferencesService(JsonSettingsStore store, Func<TimeSpan, CancellationToken, Task> delay)
    {
        this.store = store;
        this.delay = delay;
    }

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
                return delayedWorkspace is not null || !pendingSave.IsCompleted;
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

        Current = new AppPreferences(ParseTheme(root[ThemeKey])) { Workspace = ParseWorkspace(root["workspace"]) };
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
            pendingSave = SaveAfterAsync(pendingSave, root => WriteTheme(root, theme));
            return pendingSave;
        }
    }

    public void SetWorkspace(WorkspacePreferences workspace)
    {
        workspace = workspace.Normalize();
        Current = Current with { Workspace = workspace };
        Changed?.Invoke(this, Current);
        lock (saveLock)
        {
            CancelWorkspaceDelay();
            delayedWorkspace = workspace;
            workspaceDelay = new CancellationTokenSource();
            _ = SaveWorkspaceAfterQuietIntervalAsync(workspaceDelay);
        }
    }

    /// <summary>Waits for every queued save. Returns whether the last one succeeded.</summary>
    public Task<bool> FlushAsync()
    {
        lock (saveLock)
        {
            PromoteWorkspace();
            return pendingSave;
        }
    }

    async Task SaveWorkspaceAfterQuietIntervalAsync(CancellationTokenSource generation)
    {
        try
        {
            await delay(TimeSpan.FromMilliseconds(250), generation.Token).ConfigureAwait(false);
            lock (saveLock)
            {
                if (ReferenceEquals(workspaceDelay, generation)) PromoteWorkspace();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer snapshot or a shutdown flush superseded only this delay.
        }
    }

    void PromoteWorkspace()
    {
        if (delayedWorkspace is not { } snapshot) return;
        CancelWorkspaceDelay();
        delayedWorkspace = null;
        pendingSave = SaveAfterAsync(pendingSave, root => WriteWorkspace(root, snapshot));
    }

    void CancelWorkspaceDelay()
    {
        workspaceDelay?.Cancel();
        workspaceDelay?.Dispose();
        workspaceDelay = null;
    }

    async Task<bool> SaveAfterAsync(Task<bool> previous, Action<JsonObject> update)
    {
        await previous.ConfigureAwait(false);
        bool saved;
        try
        {
            await store.UpdateAsync(update).ConfigureAwait(false);
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
        WriteSchema(root);

        root[ThemeKey] = theme switch
        {
            AppTheme.Light => "light",
            AppTheme.Dark => "dark",
            _ => "system"
        };
    }

    static void WriteSchema(JsonObject root)
    {
        // A newer schema written by a later version is kept, so a downgrade does not
        // misdescribe fields it does not understand.
        if (root[SchemaVersionKey] is not JsonValue version ||
            !version.TryGetValue<double>(out var existing) ||
            existing < AppPreferences.SchemaVersion)
        {
            root[SchemaVersionKey] = AppPreferences.SchemaVersion;
        }
    }

    static WorkspacePreferences ParseWorkspace(JsonNode? node)
    {
        if (node is not JsonObject workspace || workspace["windowBounds"] is not JsonObject bounds)
            return new(null, WorkspaceWindowState.Normal);
        try
        {
            return new WorkspacePreferences(new(bounds["x"]!.GetValue<double>(), bounds["y"]!.GetValue<double>(),
                bounds["width"]!.GetValue<double>(), bounds["height"]!.GetValue<double>()),
                workspace["windowState"]?.ToString() == "maximized" ? WorkspaceWindowState.Maximized : WorkspaceWindowState.Normal).Normalize();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or NullReferenceException or OverflowException)
        {
            return new(null, WorkspaceWindowState.Normal);
        }
    }

    static void WriteWorkspace(JsonObject root, WorkspacePreferences value)
    {
        WriteSchema(root);
        var workspace = root["workspace"] as JsonObject;
        if (workspace is null) root["workspace"] = workspace = [];
        workspace["windowState"] = value.WindowState == WorkspaceWindowState.Maximized ? "maximized" : "normal";
        if (value.WindowBounds is not { } rectangle)
        {
            if (workspace["windowBounds"] is JsonObject previous)
            {
                foreach (var key in new[] { "x", "y", "width", "height" }) previous.Remove(key);
            }
            else workspace["windowBounds"] = null;
            return;
        }
        var bounds = workspace["windowBounds"] as JsonObject;
        if (bounds is null) workspace["windowBounds"] = bounds = [];
        bounds["x"] = rectangle.X;
        bounds["y"] = rectangle.Y;
        bounds["width"] = rectangle.Width;
        bounds["height"] = rectangle.Height;
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
