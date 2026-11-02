using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using SbMac.App.ViewModels.Dialogs;

namespace SbMac.App.Views.Dialogs;

public partial class SettingsDialog : Window
{
    bool waitingForSaves;
    bool savesCompleted;

    public SettingsDialog() => InitializeComponent();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Keyboard users land on Appearance, with a visible focus ring.
        AppearanceSelector.Focus(NavigationMethod.Tab);
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Every way of closing the dialog — Close, Escape, the window button — waits for the
    /// choices made in it to be written first. Shutdown is left to the app's own flush.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel || savesCompleted ||
            e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OwnerWindowClosing ||
            DataContext is not SettingsDialogViewModel { HasPendingSaves: true } viewModel)
        {
            return;
        }

        e.Cancel = true;
        if (waitingForSaves)
        {
            return;
        }

        waitingForSaves = true;
        await viewModel.CompletePendingSavesAsync();
        savesCompleted = true;
        Close();
    }
}
