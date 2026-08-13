using Avalonia.Controls;
using Avalonia.Interactivity;

using SbMac.App.ViewModels.Dialogs;

namespace SbMac.App.Views.Dialogs;

public partial class RuleEditorDialog : Window
{
    public RuleEditorDialog() => InitializeComponent();

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RuleEditorViewModel viewModel && viewModel.CanSave)
        {
            Close(viewModel.ToDefinition());
        }
    }
}
