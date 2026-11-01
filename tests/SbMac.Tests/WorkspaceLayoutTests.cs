using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using SbMac.App;
using SbMac.App.Models;
using SbMac.App.Services;
using SbMac.App.Views;
using Xunit;

namespace SbMac.Tests;

public sealed partial class WorkspaceLayoutTests : IDisposable
{
    readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("sbmac-workspace-");
    string SettingsFile => Path.Combine(directory.FullName, "settings.json");
    bool usedApplication;
    static App.App App => (App.App)Application.Current!;
    AppPreferencesService NewPreferences() => new(new JsonSettingsStore(SettingsFile));
    MainWindow Start(AppPreferencesService preferences)
    {
        usedApplication = true;
        return App.CreateMainWindow(new AppComposition(preferences, _ => new MainWindow()));
    }

    [AvaloniaFact]
    public async Task NormalWindowBoundsSurviveFreshWindowRestart()
    {
        var first = NewPreferences();
        var window = Start(first);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Position = new PixelPoint(100, 80);
        window.Width = 1120;
        window.Height = 720;
        Dispatcher.UIThread.RunJobs();
        window.Close();
        Assert.True(await first.FlushAsync());

        var restarted = NewPreferences();
        var restored = Start(restarted);
        Assert.Equal(new WindowBounds(100, 80, 1120, 720), restarted.Current.Workspace.WindowBounds);
        Assert.Equal(1120, restored.Width);
        Assert.Equal(720, restored.Height);
        Assert.Equal(new PixelPoint(100, 80), restored.Position);
        restored.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(restarted.HasPendingSaves);
        restored.Close();
        await restarted.FlushAsync();
    }

    [Fact]
    public async Task WorkspaceWritesPreserveThemeTimestampAndFutureFields()
    {
        File.WriteAllText(SettingsFile, """{"schemaVersion":7,"theme":"light","future":true,"workspace":{"future":42,"windowBounds":{"future":"keep"}}}""");
        var preferences = NewPreferences();
        Assert.Equal(AppTheme.Light, preferences.Load().Theme);
        preferences.SetWorkspace(new(new(100, 80, 1120, 720), WorkspaceWindowState.Normal));
        var timestamp = DateTimeOffset.Parse("2026-08-14T04:30:00Z");
        await new JsonUpdateCheckStateStore(SettingsFile).SetLastCheckUtcAsync(timestamp, TestContext.Current.CancellationToken);
        await preferences.SetThemeAsync(AppTheme.Dark);
        Assert.True(await preferences.FlushAsync());
        var root = JsonNode.Parse(File.ReadAllText(SettingsFile))!;
        Assert.Equal(7, root["schemaVersion"]!.GetValue<int>());
        Assert.True(root["future"]!.GetValue<bool>());
        Assert.Equal(42, root["workspace"]!["future"]!.GetValue<int>());
        Assert.Equal("keep", root["workspace"]!["windowBounds"]!["future"]!.GetValue<string>());
        Assert.Equal(timestamp, await new JsonUpdateCheckStateStore(SettingsFile).GetLastCheckUtcAsync(TestContext.Current.CancellationToken));
        Assert.Equal(AppTheme.Dark, NewPreferences().Load().Theme);
        Assert.Equal(preferences.Current.Workspace, NewPreferences().Load().Workspace);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"windowBounds\":[]}")]
    [InlineData("{\"windowBounds\":{\"x\":null,\"y\":0,\"width\":1120,\"height\":720}}")]
    [InlineData("{\"windowBounds\":{\"x\":{},\"y\":0,\"width\":1120,\"height\":720}}")]
    [InlineData("{\"windowBounds\":{\"x\":true,\"y\":0,\"width\":1120,\"height\":720}}")]
    [InlineData("{\"windowBounds\":{\"x\":0,\"y\":0,\"width\":1120}}")]
    [InlineData("{\"windowBounds\":{\"x\":\"0\",\"y\":0,\"width\":1120,\"height\":720}}")]
    [InlineData("{\"windowBounds\":{\"x\":0,\"y\":0,\"width\":0,\"height\":720}}")]
    [InlineData("{\"windowBounds\":{\"x\":0,\"y\":0,\"width\":959,\"height\":720}}")]
    [InlineData("{\"windowBounds\":{\"x\":0,\"y\":0,\"width\":1120,\"height\":599}}")]
    [InlineData("{\"windowBounds\":{\"x\":1e400,\"y\":0,\"width\":1120,\"height\":720}}")]
    [InlineData("{\"windowBounds\":{\"x\":1e100,\"y\":0,\"width\":1120,\"height\":720}}")]
    public void InvalidOrUnavailableWindowPlacementUsesSafeBounds(string workspace)
    {
        var original = "{\"theme\":\"dark\",\"workspace\":" + workspace + "}";
        File.WriteAllText(SettingsFile, original);
        var loaded = NewPreferences().Load();
        Assert.Null(loaded.Workspace.WindowBounds);
        Assert.Equal(WorkspaceWindowState.Normal, loaded.Workspace.WindowState);
        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal(original, File.ReadAllText(SettingsFile));
    }

    [AvaloniaFact]
    public void LegacyThemeOnlyFileKeepsInitialSizeAndMinimums()
    {
        File.WriteAllText(SettingsFile, """{"schemaVersion":1,"theme":"light"}""");
        var window = Start(NewPreferences());
        Assert.Equal(1360, window.Width);
        Assert.Equal(860, window.Height);
        Assert.Equal(960, window.MinWidth);
        Assert.Equal(600, window.MinHeight);
        window.Close();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(1e100)]
    public async Task InvalidPublicWorkspaceValuesCannotReachDisk(double number)
    {
        var preferences = NewPreferences();
        foreach (var bounds in new[] { new WindowBounds(number, 0, 1120, 720),
            new WindowBounds(0, 0, number, 720), new WindowBounds(0, 0, 1120, number) })
        {
            // Negative desktop coordinates remain valid; only negative sizes are invalid.
            if (bounds.X == -1) continue;
            preferences.SetWorkspace(new(bounds, WorkspaceWindowState.Maximized));
            Assert.True(await preferences.FlushAsync());
            Assert.Null(NewPreferences().Load().Workspace.WindowBounds);
            Assert.Equal(WorkspaceWindowState.Normal, NewPreferences().Load().Workspace.WindowState);
        }
    }

    [Fact]
    public void DisconnectedAndSmallDisplaysKeepTitleBarReachableAndMinimums()
    {
        var left = new WindowBounds(-1600, 30, 1600, 970);
        var primary = new WindowBounds(0, 30, 1440, 870);
        Assert.Equal(new WindowBounds(-1500, 80, 1120, 720),
            new WindowBounds(-1500, 80, 1120, 720).Constrain([primary, left]));
        var offscreen = new WindowBounds(90000, -90000, 2000, 1600).Constrain([primary]);
        Assert.Equal(new WindowBounds(0, 30, 1440, 838), offscreen);
        Assert.Equal(new WindowBounds(0, 0, 960, 600),
            new WindowBounds(5000, 5000, 1120, 720).Constrain([new(0, 0, 800, 500)]));
        var noScreens = new WindowBounds(-1200, 60, 1120, 720);
        Assert.Equal(noScreens, noScreens.Constrain([]));
    }

    [Fact]
    public void RestoreUsesEachScreensOwnScalingAndPrefersPrimaryWhenDisconnected()
    {
        // 2x primary laptop panel (2880x1800 px -> 1440x900 logical) and a 1x external to its right.
        var primary = new ScreenWorkArea(new(0, 50, 2880, 1700), 2, true);
        var external = new ScreenWorkArea(new(2880, 0, 1920, 1080), 1, false);
        var onExternal = WorkspaceLayoutService.Restore(new(3000, 100, 1120, 720), [primary, external]);
        Assert.Equal(new WindowBounds(3000, 100, 1120, 720), onExternal.Logical);
        Assert.Equal(new PixelPoint(3000, 100), onExternal.Position);
        // External unplugged: old coordinates are off-screen -> primary, converted at 2x.
        var recovered = WorkspaceLayoutService.Restore(new(3000, 100, 1120, 720), [primary]);
        Assert.Equal(new WindowBounds(320, 100, 1120, 720), recovered.Logical);
        Assert.Equal(new PixelPoint(640, 200), recovered.Position);
        // Valid visible layout on the 2x panel is preserved exactly.
        var valid = WorkspaceLayoutService.Restore(new(100, 80, 1120, 720), [primary, external]);
        Assert.Equal(new WindowBounds(100, 80, 1120, 720), valid.Logical);
        Assert.Equal(new PixelPoint(200, 160), valid.Position);
    }

    [Fact]
    public void RestoreIgnoresInvalidScreensAndFallsBackWithoutScreens()
    {
        var saved = new WindowBounds(-1200, 60, 1120, 720);
        Assert.Equal(saved, WorkspaceLayoutService.Restore(saved, []).Logical);
        var bad = new ScreenWorkArea(new(0, 0, 1440, 900), 0, true);
        Assert.Equal(saved, WorkspaceLayoutService.Restore(saved, [bad]).Logical);
        var small = new ScreenWorkArea(new(0, 0, 1600, 1000), 2, true); // 800x500 logical
        Assert.Equal(new WindowBounds(0, 0, 960, 600), WorkspaceLayoutService.Restore(saved, [small]).Logical);
    }

    [AvaloniaTheory]
    [InlineData(WindowState.Normal, false)]
    [InlineData(WindowState.Normal, true)]
    [InlineData(WindowState.Maximized, false)]
    [InlineData(WindowState.Maximized, true)]
    public async Task WindowStateRoundTripPreservesNormalBounds(WindowState state, bool minimize)
    {
        var preferences = NewPreferences();
        var window = Start(preferences);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Position = new PixelPoint(100, 80);
        window.Width = 1120;
        window.Height = 720;
        Dispatcher.UIThread.RunJobs();
        window.WindowState = state;
        Dispatcher.UIThread.RunJobs();
        if (state == WindowState.Maximized)
        {
            window.Width = 1700; // A maximized/native resize must not replace the normal rectangle.
            window.Height = 1000;
            Dispatcher.UIThread.RunJobs();
        }
        if (minimize) window.WindowState = WindowState.Minimized;
        window.Close();
        Assert.True(await preferences.FlushAsync());
        Dispatcher.UIThread.RunJobs(); // Late posts cannot requeue after close.
        Assert.False(preferences.HasPendingSaves);
        var restarted = NewPreferences();
        var restored = Start(restarted);
        Assert.Equal(new WindowBounds(100, 80, 1120, 720), restarted.Current.Workspace.WindowBounds);
        Assert.Equal(state, restored.WindowState);
        Assert.Equal(1120, restored.Width);
        Assert.Equal(720, restored.Height);
        restored.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(restarted.HasPendingSaves);
        restored.WindowState = WindowState.Normal;
        Assert.Equal(1120, restored.Width);
        restored.Close();
        await restarted.FlushAsync();
    }

    [Theory]
    [InlineData("minimized")]
    [InlineData("fullscreen")]
    [InlineData("unknown")]
    public void UnsupportedSavedStatesRestoreNormal(string state)
    {
        File.WriteAllText(SettingsFile, """{"workspace":{"windowBounds":{"x":-1200,"y":80,"width":1120,"height":720},"windowState":"STATE"}}""".Replace("STATE", state));
        var loaded = NewPreferences().Load().Workspace;
        Assert.Equal(WorkspaceWindowState.Normal, loaded.WindowState);
        Assert.Equal(-1200, loaded.WindowBounds!.X);
    }

    [Fact]
    public void RetinaRenderScalingDoesNotChangeDesktopCoordinates()
    {
        // Desktop scale 1 and render scale 2: the latter is intentionally not an input.
        Assert.Equal(new WindowBounds(100, 80, 1120, 720),
            MainWindow.DesktopBounds(new PixelPoint(100, 80), new Size(1120, 720), 1));
        Assert.Equal(new WindowBounds(100, 80, 1120, 720),
            MainWindow.DesktopBounds(new PixelPoint(200, 160), new Size(1120, 720), 2));
    }

    [Fact]
    public async Task DebouncedBurstsWriteOnceAndStaleTimersCannotPublish()
    {
        var delays = new List<TaskCompletionSource>();
        var published = new TaskCompletionSource();
        var writes = 0;
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile, _ =>
        {
            Interlocked.Increment(ref writes);
            return Task.CompletedTask;
        }), (_, _) =>
        {
            var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            delays.Add(delay);
            return delay.Task; // Deliberately ignores cancellation to exercise stale completion.
        });
        preferences.SaveCompleted += (_, _) => published.TrySetResult();
        for (var i = 0; i < 10; i++)
            preferences.SetWorkspace(new(new(i, 80, 1120, 720), WorkspaceWindowState.Normal));
        Assert.True(preferences.HasPendingSaves);
        Assert.Equal(0, writes);
        Assert.False(File.Exists(SettingsFile));
        foreach (var delay in delays) delay.SetResult();
        await published.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(await preferences.FlushAsync());
        Assert.Equal(1, writes);
        Assert.Equal(9, NewPreferences().Load().Workspace.WindowBounds!.X);
        Assert.False(preferences.HasPendingSaves);
    }

    [Fact]
    public async Task FlushDuringPublicationKeepsLatestWorkspaceAndTheme()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile, async _ =>
        {
            if (Interlocked.Increment(ref writes) != 1) return;
            entered.SetResult();
            await release.Task;
        }), (_, token) => Task.Delay(Timeout.Infinite, token));
        preferences.SetWorkspace(new(new(100, 80, 1120, 720), WorkspaceWindowState.Normal));
        var firstFlush = preferences.FlushAsync();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        preferences.SetWorkspace(new(new(200, 90, 1200, 750), WorkspaceWindowState.Maximized));
        var themeSave = preferences.SetThemeAsync(AppTheme.Dark);
        var secondFlush = preferences.FlushAsync();
        Assert.Same(secondFlush, preferences.FlushAsync());
        release.SetResult();
        Assert.True(await firstFlush);
        Assert.True(await secondFlush);
        Assert.True(await themeSave);
        Assert.Equal(2, writes); // The theme save also persists the latest dirty workspace.
        var loaded = NewPreferences().Load();
        Assert.Equal(preferences.Current, loaded);
    }

    [AvaloniaFact]
    public void ImmediateShutdownFlushesLatestDebouncedBoundsAndRunsCleanup()
    {
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile),
            (_, token) => Task.Delay(Timeout.Infinite, token));
        var window = Start(preferences);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Position = new PixelPoint(110, 90);
        window.Width = 1170;
        window.Height = 740;
        Dispatcher.UIThread.RunJobs();
        Assert.True(preferences.HasPendingSaves);
        Assert.False(File.Exists(SettingsFile));
        var cleanedUp = false;
        SbMac.App.App.CompleteShutdown(window, preferences, () => cleanedUp = true);
        Assert.True(cleanedUp);
        Assert.Equal(new WindowBounds(110, 90, 1170, 740), NewPreferences().Load().Workspace.WindowBounds);
        Assert.False(preferences.HasPendingSaves);
        window.Close();
    }

    [AvaloniaFact]
    public async Task FailedWorkspaceSaveStillRunsCleanupAndCanRecover()
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
        var cleanedUp = false;
        SbMac.App.App.CompleteShutdown(window, preferences, () => cleanedUp = true);
        Assert.True(cleanedUp);
        Assert.Equal("disk full", preferences.LastSaveError);
        Assert.False(await preferences.FlushAsync());
        Assert.Equal(1120, preferences.Current.Workspace.WindowBounds!.Width);
        fail = false;
        preferences.SetWorkspace(preferences.Current.Workspace);
        Assert.True(await preferences.FlushAsync());
        Assert.Null(preferences.LastSaveError);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ShutdownTimeoutStillRunsCleanupAndPendingPublicationCanFinish()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile, _ => release.Task));
        var window = Start(preferences);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Width = 1120;
        window.Height = 720;
        Dispatcher.UIThread.RunJobs();
        var cleanedUp = false;
        try
        {
            SbMac.App.App.CompleteShutdown(window, preferences, () => cleanedUp = true);
            Assert.True(cleanedUp);
            Assert.True(preferences.HasPendingSaves);
        }
        finally { release.TrySetResult(); }
        Assert.True(await preferences.FlushAsync());
        window.Close();
        Assert.Equal(1120, NewPreferences().Load().Workspace.WindowBounds!.Width);
    }

    [Fact]
    public async Task InvalidSnapshotClearsOwnedBoundsWithoutDiscardingFutureFields()
    {
        File.WriteAllText(SettingsFile, """{"schemaVersion":2147483648,"workspace":{"windowBounds":{"x":0,"y":0,"width":1120,"height":720,"future":"keep"}}}""");
        var preferences = NewPreferences();
        preferences.Load();
        preferences.SetWorkspace(new(new(0, 0, double.NaN, 720), WorkspaceWindowState.Normal));
        Assert.True(await preferences.FlushAsync());
        var root = JsonNode.Parse(File.ReadAllText(SettingsFile))!;
        Assert.Equal(2147483648L, root["schemaVersion"]!.GetValue<long>());
        Assert.Equal("keep", root["workspace"]!["windowBounds"]!["future"]!.GetValue<string>());
        Assert.Null(NewPreferences().Load().Workspace.WindowBounds);
    }

    public void Dispose()
    {
        if (usedApplication && Application.Current is App.App app)
        {
            app.DetachPreferences();
            app.ApplyTheme(AppTheme.System);
        }
        directory.Delete(true);
    }
}
