using Avalonia.Controls;
using Avalonia.Interactivity;

using SbMac.App.ViewModels.Dialogs;

namespace SbMac.App.Views.Dialogs;

public partial class ConnectionDialog : Window
{
    public ConnectionDialog() => InitializeComponent();

    void OnOpened(object? sender, System.EventArgs e)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        MaxHeight = System.Math.Min(MaxHeight, screen.WorkingArea.Height / screen.Scaling);
        MaxWidth = System.Math.Min(MaxWidth, screen.WorkingArea.Width / screen.Scaling);
    }

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectionDialogViewModel viewModel) return;
        var invalid = viewModel.FirstInvalidField;
        if (invalid is null)
        {
            Close(viewModel.ToConnection());
            return;
        }

        viewModel.ValidationError = "Complete the required field before saving.";
        var field = this.FindControl<Control>(invalid + "Field");
        field?.Focus();
        field?.BringIntoView();
    }
}
