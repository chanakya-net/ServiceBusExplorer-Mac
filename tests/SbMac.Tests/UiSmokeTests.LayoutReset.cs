using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using SbMac.App.Services;
using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Dialogs;
using Xunit;

namespace SbMac.Tests;

public partial class UiSmokeTests
{
    [AvaloniaFact]
    public async Task SettingsResetLayoutAppliesImmediatelyAndKeepsAppearance()
    {
        using var fixture = new AppearanceFixture();
        fixture.Seed("""{"theme":"dark","keep":true,"lastUpdateCheckUtc":"2026-08-14T04:30:00Z","bodyViewer":{"wordWrap":true},"workspace":{"windowBounds":{"x":100,"y":80,"width":1120,"height":720},"sidebarWidth":400,"detailsCollapsed":true,"activityCollapsed":false},"messageTables":{"serviceBus":{"columns":[{"id":"sequence","visible":false,"width":180}]}}}""");
        var preferences = fixture.NewPreferences();
        var owner = fixture.Start(preferences);
        owner.Show();
        var (open, dialog, _) = OpenSettings(owner);
        var button = dialog.GetControl<Button>("ResetLayoutButton");
        Assert.Equal("Reset Layout", AutomationProperties.GetName(button));
        dialog.GetControl<Button>("ResetLayoutButton").Focus();
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Assert.IsType<SettingsDialogViewModel>(dialog.DataContext).ResetLayoutCommand.ExecutionTask!;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1360, owner.Width);
        Assert.Equal(860, owner.Height);
        Assert.True(Assert.IsType<MainWindowViewModel>(owner.DataContext).IsInspectorVisible);
        Assert.Contains("SEQ", VisibleHeaders(owner.GetControl<DataGrid>("MessageGrid")));
        Assert.Equal(ThemeVariant.Dark, owner.ActualThemeVariant);
        var status = dialog.GetControl<TextBlock>("LayoutStatusText");
        Assert.True(status.IsVisible);
        Assert.Equal("Layout reset.", status.Text);
        dialog.Close();
        await open.WaitAsync(Token);
        Assert.False(preferences.HasPendingSaves);
        var root = JsonNode.Parse(File.ReadAllText(fixture.SettingsFile))!;
        Assert.Equal("dark", root["theme"]!.GetValue<string>());
        Assert.True(root["keep"]!.GetValue<bool>());
        Assert.True(root["bodyViewer"]!["wordWrap"]!.GetValue<bool>());
        Assert.Equal("2026-08-14T04:30:00Z", root["lastUpdateCheckUtc"]!.GetValue<string>());
        owner.Close();
        await preferences.FlushAsync();
        var restarted = fixture.Start(fixture.NewPreferences());
        Assert.Equal(1360, restarted.Width);
        Assert.Equal(860, restarted.Height);
        restarted.Close();
    }

    [AvaloniaFact]
    public async Task FailedResetLayoutSaysSoAndKeepsSessionDefaults()
    {
        using var fixture = new AppearanceFixture();
        var preferences = new AppPreferencesService(new JsonSettingsStore(fixture.SettingsFile,
            _ => throw new IOException("disk full")));
        var owner = fixture.Start(preferences);
        owner.Show();
        owner.Width = 1120;
        owner.Height = 720;
        var (open, dialog, _) = OpenSettings(owner);
        dialog.GetControl<Button>("ResetLayoutButton").Focus();
        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await Assert.IsType<SettingsDialogViewModel>(dialog.DataContext).ResetLayoutCommand.ExecutionTask!;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1360, owner.Width);
        var error = dialog.GetControl<TextBlock>("LayoutStatusText");
        Assert.True(error.IsVisible);
        Assert.Contains("Couldn't save", error.Text);
        Assert.Contains("not saved", Assert.IsType<MainWindowViewModel>(owner.DataContext).PreferenceSaveStatus);
        Assert.False(dialog.GetControl<TextBlock>("SaveErrorText").IsVisible);
        dialog.Close();
        await open.WaitAsync(Token);
        owner.Close();
    }
}
