using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SbMac.App.Models;
using SbMac.App.ViewModels;
using SbMac.App.Views;
using Xunit;

namespace SbMac.Tests;

public sealed partial class WorkspaceLayoutTests
{
    [AvaloniaTheory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public async Task ResetLayoutRestoresDefaultWindowAndPanelsImmediatelyAndAfterRestart(WindowState state)
    {
        var preferences = NewPreferences();
        var window = StartWithViewModel(preferences);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Width = 1120;
        window.Height = 720;
        var vm = Assert.IsType<MainWindowViewModel>(window.DataContext);
        vm.IsInspectorVisible = false;
        vm.IsActivityExpanded = true;
        window.GetControl<Grid>("MainSplitLayout").ColumnDefinitions[0].Width = new GridLength(400);
        window.CapturePanelLayout();
        window.WindowState = state;
        Dispatcher.UIThread.RunJobs();
        Assert.True(await preferences.FlushAsync());
        var save = preferences.ResetLayoutAsync();
        Dispatcher.UIThread.RunJobs();
        AssertDefaultWorkspace(window);
        Assert.True(await save);
        Assert.False(preferences.HasPendingSaves);
        Assert.Equal(WorkspacePreferences.Default, NewPreferences().Load().Workspace);
        window.Close();
        Assert.True(await preferences.FlushAsync());
        var restarted = StartWithViewModel(NewPreferences());
        restarted.Show();
        Dispatcher.UIThread.RunJobs();
        AssertDefaultWorkspace(restarted);
        restarted.Close();
    }

    static void AssertDefaultWorkspace(MainWindow window)
    {
        Assert.Equal(1360, window.Width);
        Assert.Equal(860, window.Height);
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal(280, window.GetControl<Grid>("MainSplitLayout").ColumnDefinitions[0].Width.Value);
        var vm = Assert.IsType<MainWindowViewModel>(window.DataContext);
        Assert.True(vm.IsInspectorVisible);
        Assert.False(vm.IsActivityExpanded);
        Assert.True(window.GetControl<Grid>("MessageDetailLayout").RowDefinitions[3].Height.IsStar);
    }
}
