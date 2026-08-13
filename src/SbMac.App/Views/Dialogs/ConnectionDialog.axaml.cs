using Avalonia.Controls;
using Avalonia.Interactivity;

using SbMac.App.ViewModels.Dialogs;

namespace SbMac.App.Views.Dialogs;

public partial class ConnectionDialog : Window
{
    public ConnectionDialog() => InitializeComponent();

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ConnectionDialogViewModel viewModel && viewModel.CanSave)
        {
            Close(viewModel.ToConnection());
        }
    }
}
