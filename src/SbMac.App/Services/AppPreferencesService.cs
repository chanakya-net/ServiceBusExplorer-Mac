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
    bool themeDirty;
    bool workspaceDirty;

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

    /// <summary>The last failure, retained until all changed preferences reach disk.</summary>
    public string? LastSaveError { get; private set; }

    /// <summary>An appearance failure only while the chosen theme remains unsaved.</summary>
    public string? ThemeSaveError
    {
        get { lock (saveLock) return themeDirty ? LastSaveError : null; }
    }

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
        AppPreferences updated;
        lock (saveLock)
        {
            Current = updated = Current with { Theme = theme };
            themeDirty = true;
        }
        Changed?.Invoke(this, updated);

        lock (saveLock)
        {
            pendingSave = SaveAfterAsync(pendingSave);
            return pendingSave;
        }
    }

    public void SetWorkspace(WorkspacePreferences workspace)
    {
        workspace = workspace.Normalize();
        lock (saveLock)
        {
            Current = Current with { Workspace = workspace };
            workspaceDirty = true;
        }
        Changed?.Invoke(this, Current);
        lock (saveLock)
        {
            CancelWorkspaceDelay();
            delayedWorkspace = workspace;
            workspaceDelay = new CancellationTokenSource();
            _ = SaveWorkspaceAfterQuietIntervalAsync(workspaceDelay);
        }
    }

    /// <summary>Promotes delayed work, retries unsaved changes, and waits for the save queue.</summary>
    public Task<bool> FlushAsync()
    {
        lock (saveLock)
        {
            if (delayedWorkspace is not null) PromoteWorkspace();
            else if (pendingSave.IsCompleted && (themeDirty || workspaceDirty))
                pendingSave = SaveAfterAsync(pendingSave);
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
        if (delayedWorkspace is null) return;
        CancelWorkspaceDelay();
        delayedWorkspace = null;
        pendingSave = SaveAfterAsync(pendingSave);
    }

    void CancelWorkspaceDelay()
    {
        workspaceDelay?.Cancel();
        workspaceDelay?.Dispose();
        workspaceDelay = null;
    }

    async Task<bool> SaveAfterAsync(Task<bool> previous)
    {
        await previous.ConfigureAwait(false);
        lock (saveLock)
        {
            // An earlier queued save may already have persisted these changes.
            if (!themeDirty && !workspaceDirty) return true;
        }
        bool saved;
        try
        {
            var snapshot = (Preferences: Current, Theme: false, Workspace: false);
            await store.UpdateAsync(root => snapshot = WritePendingPreferences(root)).ConfigureAwait(false);
            lock (saveLock)
            {
                if (snapshot.Theme && Current.Theme == snapshot.Preferences.Theme) themeDirty = false;
                if (snapshot.Workspace && Current.Workspace == snapshot.Preferences.Workspace) workspaceDirty = false;
                if (!themeDirty && !workspaceDirty) LastSaveError = null;
            }
            saved = true;
        }
        catch (Exception exception)
        {
            // The choice already applies for this session.
            lock (saveLock) LastSaveError = exception.Message;
            saved = false;
        }

        SaveCompleted?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    (AppPreferences Preferences, bool Theme, bool Workspace) WritePendingPreferences(JsonObject root)
    {
        lock (saveLock)
        {
            if (themeDirty) WriteTheme(root, Current.Theme);
            if (workspaceDirty) WriteWorkspace(root, Current.Workspace);
            return (Current, themeDirty, workspaceDirty);
        }
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
        if (node is not JsonObject workspace || workspace["windowBounds"] is not JsonObject bounds ||
            !TryReadNumber(bounds, "x", out var x) || !TryReadNumber(bounds, "y", out var y) ||
            !TryReadNumber(bounds, "width", out var width) || !TryReadNumber(bounds, "height", out var height))
            return new(null, WorkspaceWindowState.Normal);
        return new WorkspacePreferences(new(x, y, width, height),
            workspace["windowState"]?.ToString() == "maximized" ? WorkspaceWindowState.Maximized : WorkspaceWindowState.Normal).Normalize();
    }

    static bool TryReadNumber(JsonObject source, string key, out double number)
    {
        number = 0;
        return source[key] is JsonValue value && value.TryGetValue(out number);
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
