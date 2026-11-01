using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Dialogs;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

using SbMac.App.Models;
using SbMac.App.Services;
using SbMac.App.ViewModels;
using SbMac.App.Views;

using SbMac.Core;

namespace SbMac.App;

/// <summary>
/// What the desktop app is built from: the saved preferences, and how to build the main
/// window once they have been applied. Fixtures pass their own to avoid personal state.
/// </summary>
public sealed record AppComposition(
    AppPreferencesService Preferences,
    Func<AppPreferencesService, MainWindow> CreateMainWindow);

public partial class App : Application
{
    static readonly TimeSpan ShutdownSaveTimeout = TimeSpan.FromSeconds(2);

    readonly AppComposition? composition;
    AppPreferencesService? attachedPreferences;

    public App()
    {
    }

    /// <summary>Starts with explicit dependencies instead of the user's real settings.</summary>
    public App(AppComposition composition) => this.composition = composition;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avalonia 11 registered a DataAnnotations validator by default, which
            // duplicated every error the view models raise themselves, so it had to be
            // removed here. Avalonia 12 no longer registers it — and made BindingPlugins
            // internal — so there is nothing left to undo.
            var startup = composition ?? CreateDefaultComposition();
            var window = CreateMainWindow(startup);
            desktop.MainWindow = window;

            if (window.DataContext is MainWindowViewModel viewModel)
            {
                AddSettingsMenuItem(viewModel.OpenSettingsCommand);
            }

            // Close cleanly so in-flight sessions get disposed rather than torn down.
            desktop.ShutdownRequested += (_, _) =>
            {
                CompletePendingSaves(startup.Preferences);
                (window.DataContext as MainWindowViewModel)?.Shutdown();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Loads and applies the saved appearance, then — and only then — builds the main
    /// window, so its first frame is already in the right theme. Later preference changes
    /// apply to every open window immediately.
    /// </summary>
    public MainWindow CreateMainWindow(AppComposition startup)
    {
        ArgumentNullException.ThrowIfNull(startup);
        DetachPreferences();

        var preferences = startup.Preferences;
        ApplyTheme(preferences.Load().Theme);
        preferences.Changed += OnPreferencesChanged;
        attachedPreferences = preferences;

        return startup.CreateMainWindow(preferences);
    }

    /// <summary>Stops following the preferences attached by <see cref="CreateMainWindow"/>.</summary>
    public void DetachPreferences()
    {
        if (attachedPreferences is { } previous)
        {
            previous.Changed -= OnPreferencesChanged;
            attachedPreferences = null;
        }
    }

    /// <summary>
    /// Adds Settings… (Cmd+,) to the application menu — the one named after the app, which
    /// macOS keeps separate from the window's File/Entity/Messages/View menus. The command
    /// is set explicitly because nothing here inherits the main window's DataContext.
    /// </summary>
    public NativeMenuItem AddSettingsMenuItem(ICommand openSettings)
    {
        ArgumentNullException.ThrowIfNull(openSettings);

        var menu = NativeMenu.GetMenu(this);
        if (menu is null)
        {
            menu = CreateApplicationMenu();
            NativeMenu.SetMenu(this, menu);
        }

        var item = new NativeMenuItem("Settings…")
        {
            Command = openSettings,
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta)
        };

        menu.Add(new NativeMenuItemSeparator());
        menu.Add(item);
        return item;
    }

    /// <summary>
    /// The About entry Avalonia's macOS exporter creates when an app supplies no menu of
    /// its own. Supplying one suppresses that default, so it is recreated here; the
    /// exporter still appends Services, Hide, Hide Others, Show All and Quit after it.
    /// </summary>
    static NativeMenu CreateApplicationMenu()
    {
        var about = new NativeMenuItem("About Avalonia");
        about.Click += async (_, _) =>
        {
            if (Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
                { MainWindow: { IsVisible: true } mainWindow })
            {
                await new AboutAvaloniaDialog().ShowDialog(mainWindow);
            }
        };

        return new NativeMenu { about };
    }

    public static ThemeVariant ToThemeVariant(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default
    };

    static AppComposition CreateDefaultComposition()
    {
        var settings = new JsonSettingsStore(AppPaths.SettingsFile);
        return new AppComposition(
            new AppPreferencesService(settings),
            preferences => new MainWindow
            {
                DataContext = new MainWindowViewModel(UpdateChecker.CreateDefault(settings), preferences)
            });
    }

    void OnPreferencesChanged(object? sender, AppPreferences preferences)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyTheme(preferences.Theme);
        }
        else
        {
            // Read the latest value when the post runs, so out-of-order posts cannot win.
            var source = (AppPreferencesService)sender!;
            Dispatcher.UIThread.Post(() => ApplyTheme(source.Current.Theme));
        }
    }

    /// <summary>
    /// Applies an appearance to every window. It is set once on the application rather
    /// than per window, so open windows, dialogs opened later and System's live tracking
    /// of the macOS setting all inherit it.
    /// </summary>
    public void ApplyTheme(AppTheme theme)
    {
        RequestedThemeVariant = ToThemeVariant(theme);

        // Returning to Default clears the actual variant instead of re-reading the
        // platform's, leaving windows without a resolved theme until macOS next changes
        // appearance. Restore it exactly as Avalonia's own platform-change handler does.
        if (theme == AppTheme.System && PlatformSettings is { } platform)
        {
            SetValue(ActualThemeVariantProperty, (ThemeVariant)platform.GetColorValues().ThemeVariant);
        }
    }

    static void CompletePendingSaves(AppPreferencesService preferences)
    {
        try
        {
            // Saves never need the UI thread, so a bounded wait here cannot deadlock.
            preferences.FlushAsync().Wait(ShutdownSaveTimeout);
        }
        catch
        {
            // Quitting must not fail because a preference could not be written.
        }
    }
}
