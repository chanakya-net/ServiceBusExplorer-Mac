using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SbMac.App.Models;
using SbMac.App.Services;

namespace SbMac.App.ViewModels.Dialogs;

/// <summary>One choice in the Appearance selector.</summary>
public sealed record AppearanceOption(AppTheme Theme, string Label);

/// <summary>
/// Backs the Settings dialog. A selection applies as soon as it is made; closing the
/// dialog dismisses it rather than rolling anything back.
/// </summary>
public sealed partial class SettingsDialogViewModel : ViewModelBase
{
    const string SaveFailedMessage =
        "Couldn't save the appearance. It still applies until SB-Mac quits.";

    readonly AppPreferencesService preferences;

    public SettingsDialogViewModel(AppPreferencesService preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        this.preferences = preferences;

        // Assigned to the field so showing the current choice does not save it again.
        selectedAppearance = Appearances.First(option => option.Theme == preferences.Current.Theme);
        saveError = preferences.ThemeSaveError is null ? null : SaveFailedMessage;
    }

    public IReadOnlyList<AppearanceOption> Appearances { get; } =
    [
        new(AppTheme.System, "System"),
        new(AppTheme.Light, "Light"),
        new(AppTheme.Dark, "Dark")
    ];

    [ObservableProperty]
    AppearanceOption? selectedAppearance;

    /// <summary>Shown under the selector when the latest choice could not be written.</summary>
    [ObservableProperty]
    string? saveError;

    [ObservableProperty]
    string? layoutStatus;

    [ObservableProperty]
    bool layoutResetFailed;

    [RelayCommand]
    async Task ResetLayoutAsync()
    {
        LayoutResetFailed = !await preferences.ResetLayoutAsync();
        LayoutStatus = LayoutResetFailed
            ? "Couldn't save the layout reset. Defaults still apply until SB-Mac quits."
            : "Layout reset.";
    }

    public bool HasPendingSaves => preferences.HasPendingSaves;

    /// <summary>Waits until every choice made so far has been written (or has failed).</summary>
    public async Task CompletePendingSavesAsync()
    {
        while (preferences.HasPendingSaves)
        {
            await preferences.FlushAsync().ConfigureAwait(true);
        }
    }

    partial void OnSelectedAppearanceChanged(AppearanceOption? value)
    {
        if (value is not null)
        {
            _ = SaveAsync(value.Theme);
        }
    }

    async Task SaveAsync(AppTheme theme)
    {
        await preferences.SetThemeAsync(theme).ConfigureAwait(true);
        SaveError = preferences.ThemeSaveError is null ? null : SaveFailedMessage;
    }
}
