using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SbMac.App;
using SbMac.App.Models;
using SbMac.App.Services;
using SbMac.App.ViewModels;
using SbMac.App.Views;
using SbMac.Core.Connections;
using Xunit;

namespace SbMac.Tests;

public sealed partial class WorkspaceLayoutTests
{
    [Fact]
    public void FreshPanelPreferencesUseResetReadyDefaults()
    {
        var workspace = NewPreferences().Load().Workspace;
        Assert.Equal(280d, workspace.SidebarWidth);
        Assert.Null(workspace.DetailsHeight);
        Assert.False(workspace.DetailsCollapsed);
        Assert.True(workspace.ActivityCollapsed);
    }

    [Fact]
    public async Task PanelPreferencesSurviveRestartIndependently()
    {
        var first = NewPreferences();
        first.SetWorkspace(new(null, WorkspaceWindowState.Normal, 360d, 260d, true, false));
        Assert.True(await first.FlushAsync());

        var workspace = NewPreferences().Load().Workspace;
        Assert.Equal(360d, workspace.SidebarWidth);
        Assert.Equal(260d, workspace.DetailsHeight);
        Assert.True(workspace.DetailsCollapsed);
        Assert.False(workspace.ActivityCollapsed);
    }

    [Theory]
    [InlineData("1e9", 560d)]
    [InlineData("-5", 200d)]
    [InlineData("\"wide\"", 280d)]
    public void OutOfRangeSidebarWidthIsClampedOrDefaulted(string value, double expected)
    {
        var original = "{\"workspace\":{\"sidebarWidth\":" + value + "}}";
        File.WriteAllText(SettingsFile, original);
        Assert.Equal(expected, NewPreferences().Load().Workspace.SidebarWidth);
        Assert.Equal(original, File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void OversizedDetailsHeightIsClampedAndNonNumericIsUnset()
    {
        File.WriteAllText(SettingsFile, """{"workspace":{"detailsHeight":1e9}}""");
        Assert.Equal(4000d, NewPreferences().Load().Workspace.DetailsHeight);

        File.WriteAllText(SettingsFile, """{"workspace":{"detailsHeight":"tall"}}""");
        Assert.Null(NewPreferences().Load().Workspace.DetailsHeight);
    }

    [Fact]
    public async Task PanelWritesPreserveUnknownWorkspaceAndThemeKeys()
    {
        File.WriteAllText(SettingsFile, """{"theme":"dark","workspace":{"future":42}}""");
        var preferences = NewPreferences();
        preferences.SetWorkspace(new(null, WorkspaceWindowState.Normal, 340d, 300d, true, false));
        Assert.True(await preferences.FlushAsync());

        var root = JsonNode.Parse(File.ReadAllText(SettingsFile))!;
        Assert.Equal("dark", root["theme"]!.GetValue<string>());
        Assert.Equal(42, root["workspace"]!["future"]!.GetValue<int>());
        Assert.Equal(340d, root["workspace"]!["sidebarWidth"]!.GetValue<double>());
        Assert.Equal(300d, root["workspace"]!["detailsHeight"]!.GetValue<double>());
    }

    [AvaloniaFact]
    public async Task CollapsedPanelsAndSidebarRestoreAfterRestartIndependently()
    {
        var first = NewPreferences();
        var window = StartWithViewModel(first);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var viewModel = (MainWindowViewModel)window.DataContext!;
        viewModel.ToggleInspectorCommand.Execute(null);
        viewModel.IsActivityExpanded = true;
        window.GetControl<Grid>("MainSplitLayout").ColumnDefinitions[0].Width = new(400, GridUnitType.Pixel);
        window.UpdateLayout();
        window.CapturePanelLayout();
        window.Close();
        Assert.True(await first.FlushAsync());

        var restarted = StartWithViewModel(NewPreferences());
        restarted.Show();
        Dispatcher.UIThread.RunJobs();
        var restoredModel = (MainWindowViewModel)restarted.DataContext!;
        Assert.False(restoredModel.IsInspectorVisible);
        Assert.True(restoredModel.IsActivityExpanded);
        Assert.Equal(400d, restarted.GetControl<Grid>("MainSplitLayout").ColumnDefinitions[0].Width.Value);
        restarted.Close();
    }

    [AvaloniaFact]
    public void OversizedSavedPanelsAreClampedToUsableLayout()
    {
        File.WriteAllText(SettingsFile, """{"workspace":{"sidebarWidth":9000,"detailsHeight":9000}}""");
        var window = StartWithViewModel(NewPreferences());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs(); // Clamping runs in SizeChanged; the next pass applies it.
        window.UpdateLayout();

        var split = window.GetControl<Grid>("MainSplitLayout");
        var details = window.GetControl<Grid>("MessageDetailLayout");
        Assert.True(details.Bounds.Height > 0, "Layout pass produced no height to clamp against.");
        Assert.InRange(split.ColumnDefinitions[0].ActualWidth, 200d, 560d);
        Assert.True(split.Bounds.Width - split.ColumnDefinitions[0].ActualWidth >= 420d);
        Assert.True(details.RowDefinitions[1].ActualHeight >= 220d, "Message table lost usable height.");
        Assert.True(details.RowDefinitions[3].ActualHeight <= details.Bounds.Height - 220d);
        window.Close();
    }

    MainWindow StartWithViewModel(AppPreferencesService preferences)
    {
        usedApplication = true;
        var store = new ConnectionStore(Path.Combine(directory.FullName, "connections.json"),
            new FileSecretStore(Path.Combine(directory.FullName, "secrets.json")));
        return App.CreateMainWindow(new AppComposition(preferences, _ => new MainWindow
        {
            DataContext = MainWindowViewModel.CreateIsolated(store,
                (_, _) => throw new InvalidOperationException("Unexpected network call"), preferences)
        }));
    }
}
