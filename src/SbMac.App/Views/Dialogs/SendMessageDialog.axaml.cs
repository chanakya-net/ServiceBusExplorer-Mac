using Avalonia.Controls;
using Avalonia.Interactivity;

using SbMac.App.ViewModels.Dialogs;

namespace SbMac.App.Views.Dialogs;

public partial class SendMessageDialog : Window
{
    public SendMessageDialog() => InitializeComponent();

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    void OnSend(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SendMessageViewModel viewModel)
        {
            return;
        }

        // Build() reports its own validation errors into the dialog, so a null result
        // means the user still has something to fix — keep the window open.
        if (viewModel.Build() is { } result)
        {
            Close(result);
        }
    }
}
