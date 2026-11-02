using System.Text.Json.Nodes;
using SbMac.App.Models;
using SbMac.App.Services;
using SbMac.Core.Connections;
using Xunit;

namespace SbMac.Tests;

public sealed partial class AppPreferencesTests
{
    [Fact]
    public async Task ResetLayoutClearsPlacementPanelsAndEveryTableContextButKeepsEverythingElse()
    {
        File.WriteAllText(SettingsFile, """
            {"schemaVersion":9,"theme":"dark","lastUpdateCheckUtc":"2026-08-14T04:30:00Z",
             "bodyViewer":{"wordWrap":true},"future":{"keep":[1]},
             "workspace":{"future":42,"windowState":"maximized","windowBounds":{"x":100,"y":80,"width":1120,"height":720,"future":"keep"},"sidebarWidth":400,"detailsHeight":300,"detailsCollapsed":true,"activityCollapsed":false},
             "messageTables":{"future":{"x":1},"serviceBus":{"other":2,"columns":[{"id":"sequence","visible":false}]},"deadLetter":{"columns":[{"id":"reason","visible":false}]},"eventHubs":{"columns":[{"id":"offset","visible":false}]}}}
            """);
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile));
        preferences.Load();
        var changed = 0;
        var reset = 0;
        preferences.Changed += (_, value) => { Assert.Equal(WorkspacePreferences.Default, value.Workspace); changed++; };
        preferences.LayoutReset += (_, _) => { Assert.Equal(WorkspacePreferences.Default, preferences.Current.Workspace); reset++; };
        Assert.True(await preferences.ResetLayoutAsync());
        Assert.Equal(1, changed);
        Assert.Equal(1, reset);
        Assert.Equal(WorkspacePreferences.Default, preferences.Current.Workspace);
        Assert.All(preferences.Current.MessageTables.Values, Assert.Empty);
        var restarted = new AppPreferencesService(new JsonSettingsStore(SettingsFile)).Load();
        Assert.Equal(WorkspacePreferences.Default, restarted.Workspace);
        Assert.All(restarted.MessageTables.Values, Assert.Empty);
        Assert.Equal(AppTheme.Dark, restarted.Theme);
        AssertResetPreservesUnrelatedSettings(ReadRoot());
        Assert.Equal(Timestamp, await new JsonUpdateCheckStateStore(SettingsFile).GetLastCheckUtcAsync(Token));
    }

    static void AssertResetPreservesUnrelatedSettings(JsonObject root)
    {
        Assert.Equal(9, root["schemaVersion"]!.GetValue<int>());
        Assert.True(root["bodyViewer"]!["wordWrap"]!.GetValue<bool>());
        Assert.Equal("[1]", root["future"]!["keep"]!.ToJsonString());
        Assert.Equal(42, root["workspace"]!["future"]!.GetValue<int>());
        Assert.Equal("keep", root["workspace"]!["windowBounds"]!["future"]!.GetValue<string>());
        Assert.Null(root["workspace"]!["windowBounds"]!["x"]);
        Assert.Equal(1, root["messageTables"]!["future"]!["x"]!.GetValue<int>());
        Assert.Equal(2, root["messageTables"]!["serviceBus"]!["other"]!.GetValue<int>());
        foreach (var key in new[] { "serviceBus", "deadLetter", "eventHubs" })
            Assert.Null(root["messageTables"]![key]!["columns"]);
    }

    [Fact]
    public async Task ResetLayoutDoesNotCreateMissingTableContainers()
    {
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile));
        Assert.True(await preferences.ResetLayoutAsync());
        Assert.Null(ReadRoot()["messageTables"]);
    }

    [Fact]
    public async Task ResetLayoutSupersedesDebouncedWorkspaceAndLaterColumnChangesStillSave()
    {
        var delayed = new TaskCompletionSource(); // Ignores cancellation: a stale timer must be harmless.
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile), (_, _) => delayed.Task);
        preferences.SetWorkspace(new(new(100, 80, 1120, 720), WorkspaceWindowState.Normal, 400));
        Assert.True(await preferences.ResetLayoutAsync());
        delayed.SetResult();
        Assert.True(await preferences.FlushAsync());
        Assert.Equal(WorkspacePreferences.Default, new AppPreferencesService(new JsonSettingsStore(SettingsFile)).Load().Workspace);
        Assert.True(await preferences.SetMessageColumnsAsync("serviceBus", [new("sequence", false, 2, 150)]));
        var root = ReadRoot();
        Assert.NotNull(root["messageTables"]!["serviceBus"]!["columns"]);
        Assert.Null(root["messageTables"]!["deadLetter"]);
        Assert.Null(root["messageTables"]!["eventHubs"]);
        Assert.False(preferences.HasPendingSaves);
    }

    [Fact]
    public async Task ResetLayoutFollowsInFlightSavesAndLaterWorkspaceChangesWin()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile, async _ =>
        {
            if (Interlocked.Increment(ref writes) != 1) return;
            entered.SetResult();
            await release.Task;
        }));
        var earlier = preferences.SetMessageColumnsAsync("serviceBus", [new("sequence", false, 2, 150)]);
        await entered.Task.WaitAsync(Token);
        var reset = preferences.ResetLayoutAsync();
        Assert.False(reset.IsCompleted);
        release.SetResult();
        Assert.True(await earlier);
        Assert.True(await reset);
        Assert.Null(ReadRoot()["messageTables"]!["serviceBus"]!["columns"]);
        Assert.Equal(WorkspacePreferences.Default, new AppPreferencesService(new JsonSettingsStore(SettingsFile)).Load().Workspace);
        var later = new WorkspacePreferences(new(100, 80, 1120, 720), WorkspaceWindowState.Normal, 360);
        preferences.SetWorkspace(later);
        Assert.True(await preferences.FlushAsync());
        Assert.Equal(later, new AppPreferencesService(new JsonSettingsStore(SettingsFile)).Load().Workspace);
        Assert.False(preferences.HasPendingSaves);
    }

    [Fact]
    public async Task ResetLayoutLeavesIsolatedConnectionsAndSecretsIntact()
    {
        var connectionFile = Path.Combine(directory.FullName, "connections.json");
        var secretFile = Path.Combine(directory.FullName, "secrets.json");
        var connections = new ConnectionStore(connectionFile, new FileSecretStore(secretFile));
        var seed = new NamespaceConnection { Name = "fixture", ConnectionString = "synthetic-connection-secret" };
        await connections.SaveAsync([seed], Token);
        var connectionBytes = File.ReadAllBytes(connectionFile);
        var secretBytes = File.ReadAllBytes(secretFile);
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile));
        Assert.True(await preferences.ResetLayoutAsync());
        Assert.Equal(connectionBytes, File.ReadAllBytes(connectionFile));
        Assert.Equal(secretBytes, File.ReadAllBytes(secretFile));
        var restored = Assert.Single(await connections.LoadAsync(Token));
        Assert.Equal(seed.Id, restored.Id);
        Assert.Equal(seed.ConnectionString, restored.ConnectionString);
    }

    [Fact]
    public async Task FailedResetKeepsInMemoryDefaultsAndReportsTheError()
    {
        File.WriteAllText(Path.Combine(directory.FullName, "blocked"), "not a directory");
        var preferences = new AppPreferencesService(new JsonSettingsStore(Path.Combine(directory.FullName, "blocked", "settings.json")));
        preferences.SetWorkspace(new(new(100, 80, 1120, 720), WorkspaceWindowState.Normal, 400));
        Assert.False(await preferences.ResetLayoutAsync());
        Assert.Equal(WorkspacePreferences.Default, preferences.Current.Workspace);
        Assert.All(preferences.Current.MessageTables.Values, Assert.Empty);
        Assert.NotNull(preferences.LastSaveError);
        Assert.Null(preferences.ThemeSaveError);
        Assert.False(await preferences.FlushAsync());
    }
}
