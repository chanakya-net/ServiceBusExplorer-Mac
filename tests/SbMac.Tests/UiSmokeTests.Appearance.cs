using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Avalonia.Threading;

using SbMac.App;
using SbMac.App.Models;
using SbMac.App.Services;
using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Dialogs;
using SbMac.App.Views;
using SbMac.App.Views.Dialogs;

using Xunit;

namespace SbMac.Tests;

/// <summary>
/// Saved appearance: restored before the main window exists, switched live from Settings,
/// and reflected by colours that were resolved before the switch.
/// </summary>
public partial class UiSmokeTests
{
    static App.App CurrentApp => Assert.IsType<App.App>(Application.Current);

    /// <summary>Temporary settings, plus cleanup of the application-wide theme it changes.</summary>
    sealed class AppearanceFixture : IDisposable
    {
        readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("sbmac-appearance-");

        public string SettingsFile => Path.Combine(directory.FullName, "settings.json");

        public AppPreferencesService NewPreferences() => new(new JsonSettingsStore(SettingsFile));

        public void Seed(string json) => File.WriteAllText(SettingsFile, json);

        public MainWindow Start(AppPreferencesService preferences) =>
            CurrentApp.CreateMainWindow(new AppComposition(preferences,
                loaded => new MainWindow { DataContext = NewIsolatedViewModel(loaded) }));

        public void Dispose()
        {
            CurrentApp.DetachPreferences();
            CurrentApp.ApplyTheme(AppTheme.System);
            directory.Delete(recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData("light", "Light")]
    [InlineData("dark", "Dark")]
    [InlineData("system", "Default")]
    public void SavedAppearanceIsAppliedBeforeMainWindowConstruction(string saved, string expectedVariant)
    {
        using var fixture = new AppearanceFixture();
        fixture.Seed($$"""{"schemaVersion":1,"theme":"{{saved}}"}""");
        CurrentApp.RequestedThemeVariant = saved == "dark" ? ThemeVariant.Light : ThemeVariant.Dark;
        ThemeVariant? requestedAtConstruction = null;
        ThemeVariant? actualWhenOpened = null;

        var window = CurrentApp.CreateMainWindow(new AppComposition(fixture.NewPreferences(), preferences =>
        {
            requestedAtConstruction = CurrentApp.RequestedThemeVariant;
            var created = new MainWindow { DataContext = NewIsolatedViewModel(preferences) };
            created.Opened += (_, _) => actualWhenOpened = created.ActualThemeVariant;
            return created;
        }));
        window.Show();

        Assert.Equal(expectedVariant, requestedAtConstruction?.ToString());
        Assert.Equal(saved == "system" ? PlatformVariant : (expectedVariant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light), actualWhenOpened);

        window.Close();
    }

    [AvaloniaFact]
    public async Task PreferenceChangesApplyToTheApplicationImmediately()
    {
        using var fixture = new AppearanceFixture();
        var preferences = fixture.NewPreferences();
        var window = fixture.Start(preferences);
        window.Show();

        var save = preferences.SetThemeAsync(AppTheme.Dark);
        Assert.Equal(ThemeVariant.Dark, CurrentApp.RequestedThemeVariant);
        Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);

        _ = preferences.SetThemeAsync(AppTheme.System);
        Assert.Equal(ThemeVariant.Default, CurrentApp.RequestedThemeVariant);
        Assert.Equal(PlatformVariant, window.ActualThemeVariant);

        Assert.True(await save);
        Assert.True(await preferences.FlushAsync());
        window.Close();
    }

    [AvaloniaFact]
    public async Task DetachedPreferencesNoLongerDriveTheApplication()
    {
        using var fixture = new AppearanceFixture();
        var preferences = fixture.NewPreferences();
        fixture.Start(preferences).Close();

        CurrentApp.DetachPreferences();
        var save = preferences.SetThemeAsync(AppTheme.Dark);

        Assert.Equal(ThemeVariant.Default, CurrentApp.RequestedThemeVariant);
        Assert.True(await save);
    }

    static (Task Open, SettingsDialog Dialog, ComboBox Selector) OpenSettings(MainWindow owner)
    {
        var viewModel = Assert.IsType<MainWindowViewModel>(owner.DataContext);
        Assert.True(viewModel.OpenSettingsCommand.CanExecute(null));
        var open = viewModel.OpenSettingsCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<SettingsDialog>(Assert.Single(owner.OwnedWindows));
        Dispatcher.UIThread.RunJobs();
        return (open, dialog, dialog.GetControl<ComboBox>("AppearanceSelector"));
    }

    static string SelectedLabel(ComboBox selector) =>
        Assert.IsType<AppearanceOption>(selector.SelectedItem).Label;

    [AvaloniaFact]
    public async Task SettingsSelectionAppliesImmediatelyAndSurvivesReopen()
    {
        using var fixture = new AppearanceFixture();
        fixture.Seed("""{"lastUpdateCheckUtc":"2026-08-14T04:30:00.0000000Z","future":{"keep":[1]}}""");
        var owner = fixture.Start(fixture.NewPreferences());
        owner.Show();

        var (open, dialog, selector) = OpenSettings(owner);
        Assert.Equal("Appearance", AutomationProperties.GetName(selector));
        Assert.Equal(["System", "Light", "Dark"],
            selector.Items.Cast<AppearanceOption>().Select(option => option.Label));
        Assert.Equal("System", SelectedLabel(selector));
        Assert.True(selector.IsFocused);
        Assert.DoesNotContain("theme", File.ReadAllText(fixture.SettingsFile));

        selector.SelectedIndex = 2;
        Assert.Equal(ThemeVariant.Dark, owner.ActualThemeVariant);
        Assert.Equal(ThemeVariant.Dark, dialog.ActualThemeVariant);

        // Rapid changes: the last one wins, on screen and on disk.
        selector.SelectedIndex = 0;
        selector.SelectedIndex = 1;
        Assert.Equal(ThemeVariant.Light, owner.ActualThemeVariant);

        dialog.GetControl<Button>("CloseButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await open.WaitAsync(Token);
        Assert.Empty(owner.OwnedWindows);
        Assert.Contains("\"theme\": \"light\"", File.ReadAllText(fixture.SettingsFile));
        Assert.Contains("\"keep\"", File.ReadAllText(fixture.SettingsFile));
        owner.Close();

        // A fresh service is what the next launch sees.
        var restarted = fixture.NewPreferences();
        var reopenedOwner = fixture.Start(restarted);
        Assert.Equal(ThemeVariant.Light, CurrentApp.RequestedThemeVariant);
        reopenedOwner.Show();
        var reopened = OpenSettings(reopenedOwner);
        Assert.Equal("Light", SelectedLabel(reopened.Selector));
        Assert.False(restarted.HasPendingSaves);
        reopened.Dialog.Close();
        await reopened.Open.WaitAsync(Token);
        reopenedOwner.Close();
    }

    [AvaloniaFact]
    public async Task EscapeDismissesSettingsWithoutRollingBack()
    {
        using var fixture = new AppearanceFixture();
        var owner = fixture.Start(fixture.NewPreferences());
        owner.Show();
        var (open, dialog, selector) = OpenSettings(owner);

        selector.SelectedIndex = 2;
        dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await open.WaitAsync(Token);

        Assert.Empty(owner.OwnedWindows);
        Assert.Equal(ThemeVariant.Dark, CurrentApp.RequestedThemeVariant);
        Assert.Contains("dark", File.ReadAllText(fixture.SettingsFile));
        owner.Close();
    }

    [AvaloniaFact]
    public async Task SettingsKeyboardNavigationReachesClose()
    {
        using var fixture = new AppearanceFixture();
        var owner = fixture.Start(fixture.NewPreferences());
        owner.Show();
        var (open, dialog, selector) = OpenSettings(owner);

        dialog.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Assert.Equal("Light", SelectedLabel(selector));
        Assert.Equal(ThemeVariant.Light, CurrentApp.RequestedThemeVariant);

        dialog.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.True(dialog.GetControl<Button>("CloseButton").IsFocused);

        dialog.Close();
        await open.WaitAsync(Token);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task FailedSaveKeepsSettingsUsableAndSaysSo()
    {
        using var fixture = new AppearanceFixture();
        var blocked = Path.Combine(Path.GetDirectoryName(fixture.SettingsFile)!, "blocked");
        File.WriteAllText(blocked, "a file where a directory is needed");
        var preferences = new AppPreferencesService(new JsonSettingsStore(Path.Combine(blocked, "settings.json")));
        var owner = fixture.Start(preferences);
        owner.Show();
        var (open, dialog, selector) = OpenSettings(owner);
        var error = dialog.GetControl<TextBlock>("SaveErrorText");
        Assert.False(error.IsVisible);

        selector.SelectedIndex = 2;
        Assert.False(await preferences.FlushAsync().WaitAsync(Token));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(ThemeVariant.Dark, owner.ActualThemeVariant);
        Assert.True(error.IsVisible);
        Assert.Contains("Couldn't save", error.Text);

        dialog.Close();
        await open.WaitAsync(Token);
        Assert.Empty(owner.OwnedWindows);
        owner.Close();
    }

    [AvaloniaFact]
    public void SettingsIsUnavailableWithoutInjectedPreferences()
    {
        var viewModel = NewIsolatedViewModel();

        Assert.False(viewModel.OpenSettingsCommand.CanExecute(null));
    }

    /// <summary>What System resolves to: the platform's current appearance.</summary>
    static ThemeVariant PlatformVariant =>
        (ThemeVariant)Assert.IsAssignableFrom<Avalonia.Platform.IPlatformSettings>(CurrentApp.PlatformSettings)
            .GetColorValues().ThemeVariant;

    static Color ThemeColor(string key, ThemeVariant variant)
    {
        Assert.True(CurrentApp.Resources.TryGetResource(key, variant, out var resource));
        return Assert.IsAssignableFrom<ISolidColorBrush>(resource).Color;
    }

    static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    static void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    [AvaloniaFact]
    public async Task AppearanceChangeRefreshesExistingActivityAndDialogColors()
    {
        using var fixture = new AppearanceFixture();
        fixture.Seed("""{"theme":"light"}""");
        var preferences = fixture.NewPreferences();
        var owner = fixture.Start(preferences);
        var viewModel = Assert.IsType<MainWindowViewModel>(owner.DataContext);
        owner.Show();

        var purging = new OperationViewModel(OperationKind.Purge, "Purging orders…");
        purging.Report("4,000 deleted", 4_000, 20_000);
        var failed = new OperationViewModel(OperationKind.Connect, "Connecting to contoso…");
        failed.Finish(OperationState.Failed, "No such host is known.");
        viewModel.Operations.Add(purging);
        viewModel.Operations.Add(failed);
        viewModel.IsActivityExpanded = true;
        var error = owner.ShowErrorAsync("Peek failed", "The entity was not found.");
        var confirm = owner.ConfirmAsync("Purge", "Delete everything?", "Purge", destructive: true);
        Settle();

        var bars = owner.GetVisualDescendants().OfType<ProgressBar>().ToList();
        Assert.Equal(4, bars.Count);
        var dialogs = owner.OwnedWindows.OfType<MessageDialog>().ToList();
        var heading = dialogs[0].GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == "Peek failed");
        var purge = dialogs[1].GetVisualDescendants().OfType<Button>().First(button => Equals(button.Content, "Purge"));

        foreach (var (theme, variant) in new[] { (AppTheme.Dark, ThemeVariant.Dark), (AppTheme.Light, ThemeVariant.Light), (AppTheme.System, PlatformVariant) })
        {
            _ = preferences.SetThemeAsync(theme);
            Settle();
            var actual = CurrentApp.ActualThemeVariant;
            Assert.Equal(variant, actual);

            Assert.All(bars, bar => Assert.Equal(
                ThemeColor(((OperationViewModel)bar.DataContext!).AccentKey, actual), ColorOf(bar.Foreground)));
            Assert.Equal(ThemeColor("DangerBrush", actual), ColorOf(heading.Foreground));
            Assert.Equal(ThemeColor("DangerBrush", actual), ColorOf(purge.Foreground));
        }

        // Colour is never the only cue: the failed row still says so in words.
        Assert.Contains(owner.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text == failed.StateLabel && text.IsEffectivelyVisible);

        foreach (var dialog in dialogs)
        {
            dialog.Close();
        }

        await Task.WhenAll(error, confirm).WaitAsync(Token);
        Assert.True(await preferences.FlushAsync());
        owner.Close();
    }

    [AvaloniaFact]
    public void ApplicationSettingsMenuInvokesDialogBoundary()
    {
        using var fixture = new AppearanceFixture();
        var owner = fixture.Start(fixture.NewPreferences());
        var viewModel = Assert.IsType<MainWindowViewModel>(owner.DataContext);
        owner.Show();
        var existing = NativeMenu.GetMenu(CurrentApp);
        var existingCount = existing?.Items.Count ?? 0;
        try
        {
            var item = CurrentApp.AddSettingsMenuItem(viewModel.OpenSettingsCommand);

            var menu = Assert.IsType<NativeMenu>(NativeMenu.GetMenu(CurrentApp));
            Assert.Same(item, menu.Items.Last());
            Assert.Equal("Settings…", item.Header);
            Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Meta), item.Gesture);
            Assert.Same(viewModel.OpenSettingsCommand, item.Command);
            Assert.True(item.Command!.CanExecute(null));
            if (existing is null)
            {
                // Supplying an application menu must not lose the About entry Avalonia adds.
                Assert.Equal("About Avalonia", Assert.IsType<NativeMenuItem>(menu.Items[0]).Header);
            }

            // The path the macOS exporter takes for a click or the Cmd+, key equivalent.
            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            var dialog = Assert.IsType<SettingsDialog>(Assert.Single(owner.OwnedWindows));
            Assert.False(item.Command.CanExecute(null));
            ((INativeMenuItemExporterEventsImplBridge)item).RaiseClicked();
            Assert.Single(owner.OwnedWindows);

            dialog.Close();
            Settle();
            Assert.Empty(owner.OwnedWindows);
            Assert.True(item.Command.CanExecute(null));
        }
        finally
        {
            RestoreApplicationMenu(existing, existingCount);
            owner.Close();
        }
    }

    static void RestoreApplicationMenu(NativeMenu? existing, int existingCount)
    {
        if (existing is null)
        {
            NativeMenu.SetMenu(CurrentApp, null);
            return;
        }

        while (existing.Items.Count > existingCount)
        {
            existing.Items.RemoveAt(existing.Items.Count - 1);
        }
    }

    static CancellationToken Token => TestContext.Current.CancellationToken;
}
