using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SbMac.App.Models;
using SbMac.App.Services;
using SbMac.App.ViewModels.Dialogs;
using Xunit;

namespace SbMac.Tests;

public sealed partial class WorkspaceLayoutTests
{
    [Fact]
    public async Task AlreadyPersistedQueuedChangesDoNotCreateSpuriousSaveFailures()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile, async _ =>
        {
            var write = Interlocked.Increment(ref writes);
            if (write == 3) throw new IOException("disk full after preferences were saved");
            if (write != 1) return;
            entered.TrySetResult();
            await release.Task;
        }), (_, token) => Task.Delay(Timeout.Infinite, token));
        preferences.SetWorkspace(new(new(100, 80, 1120, 720), WorkspaceWindowState.Normal));
        var first = preferences.FlushAsync();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        preferences.SetWorkspace(new(new(200, 90, 1200, 750), WorkspaceWindowState.Maximized));
        var theme = preferences.SetThemeAsync(AppTheme.Dark);
        var flush = preferences.FlushAsync();
        release.TrySetResult();
        Assert.True(await first);
        Assert.True(await theme);
        Assert.True(await flush);
        Assert.Equal(preferences.Current, NewPreferences().Load());
        Assert.Null(preferences.LastSaveError);
    }

    [AvaloniaFact]
    public async Task ShutdownRetriesFailedWorkspaceEvenWhenWindowBoundsAreUnchanged()
    {
        var fail = true;
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile,
            _ => fail ? throw new IOException("disk full") : Task.CompletedTask));
        var window = Start(preferences);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Width = 1120;
        window.Height = 720;
        Dispatcher.UIThread.RunJobs();
        Assert.False(await preferences.FlushAsync());
        fail = false;
        var cleanedUp = false;
        try
        {
            SbMac.App.App.CompleteShutdown(window, preferences, () => cleanedUp = true);
            Assert.True(cleanedUp);
            Assert.Equal(preferences.Current.Workspace, NewPreferences().Load().Workspace);
            Assert.Null(preferences.LastSaveError);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task ThemeSaveRetriesFailedWorkspaceWithoutDiscardingUntouchedFields()
    {
        File.WriteAllText(SettingsFile, """{"schemaVersion":7,"theme":"sepia","workspace":{"windowBounds":{"future":true}},"lastUpdateCheckUtc":"2026-08-14T04:30:00Z"}""");
        var fail = true;
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile,
            _ => fail ? throw new IOException("disk full") : Task.CompletedTask));
        preferences.Load();
        preferences.SetWorkspace(new(new(100, 80, 1120, 720), WorkspaceWindowState.Normal));
        Assert.False(await preferences.FlushAsync());
        Assert.Equal("disk full", preferences.LastSaveError);
        Assert.Equal("sepia", JsonNode.Parse(File.ReadAllText(SettingsFile))!["theme"]!.GetValue<string>());
        fail = false;
        Assert.True(await preferences.SetThemeAsync(AppTheme.Dark));
        Assert.Equal(preferences.Current, NewPreferences().Load());
        Assert.Null(preferences.LastSaveError);
        var root = JsonNode.Parse(File.ReadAllText(SettingsFile))!;
        Assert.Equal(7, root["schemaVersion"]!.GetValue<int>());
        Assert.True(root["workspace"]!["windowBounds"]!["future"]!.GetValue<bool>());
        Assert.Equal("2026-08-14T04:30:00Z", root["lastUpdateCheckUtc"]!.GetValue<string>());
    }

    [Fact]
    public async Task WorkspaceOnlyFailureDoesNotReportAnAppearanceFailure()
    {
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile,
            _ => throw new IOException("disk full")));
        preferences.SetWorkspace(new(new(100, 80, 1120, 720), WorkspaceWindowState.Normal));
        Assert.False(await preferences.FlushAsync());
        Assert.Equal("disk full", preferences.LastSaveError);
        Assert.Null(new SettingsDialogViewModel(preferences).SaveError);
    }

    [Fact]
    public async Task WorkspaceSaveRetriesFailedThemeBeforeClearingFailureStatus()
    {
        File.WriteAllText(SettingsFile, """{"schemaVersion":7,"theme":"light","future":true,"workspace":{"future":42,"windowBounds":{"future":"keep"}}}""");
        var fail = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile, async _ =>
        {
            if (fail) throw new IOException("disk full");
            entered.TrySetResult();
            await release.Task;
        }));
        preferences.Load();
        Assert.False(await preferences.SetThemeAsync(AppTheme.Dark));
        Assert.Equal("disk full", preferences.LastSaveError);
        var timestamp = DateTimeOffset.Parse("2026-08-14T04:30:00Z");
        await new JsonUpdateCheckStateStore(SettingsFile).SetLastCheckUtcAsync(timestamp, TestContext.Current.CancellationToken);
        fail = false;
        preferences.SetWorkspace(new(new(100, 80, 1120, 720), WorkspaceWindowState.Normal));
        var save = preferences.FlushAsync();
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(preferences.LastSaveError);
            Assert.Equal(AppTheme.Light, NewPreferences().Load().Theme);
        }
        finally { release.TrySetResult(); }
        Assert.True(await save);
        Assert.Equal(preferences.Current, NewPreferences().Load());
        Assert.Null(preferences.LastSaveError);
        var root = JsonNode.Parse(File.ReadAllText(SettingsFile))!;
        Assert.Equal(7, root["schemaVersion"]!.GetValue<int>());
        Assert.True(root["future"]!.GetValue<bool>());
        Assert.Equal(42, root["workspace"]!["future"]!.GetValue<int>());
        Assert.Equal("keep", root["workspace"]!["windowBounds"]!["future"]!.GetValue<string>());
        Assert.Equal(timestamp, await new JsonUpdateCheckStateStore(SettingsFile).GetLastCheckUtcAsync(TestContext.Current.CancellationToken));
    }
}
