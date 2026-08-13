using Avalonia.Controls;
using Avalonia.Interactivity;

using SbMac.App.ViewModels.Dialogs;

namespace SbMac.App.Views.Dialogs;

public partial class QueueEditorDialog : Window
{
    public QueueEditorDialog() => InitializeComponent();

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is QueueEditorViewModel viewModel && viewModel.CanSave)
        {
            Close(viewModel.ToDefinition());
        }
    }
}
