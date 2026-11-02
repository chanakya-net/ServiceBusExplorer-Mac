using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;

using SbMac.App.ViewModels.Dialogs;

namespace SbMac.App.Views.Dialogs;

public partial class TopicEditorDialog : Window
{
    bool isOpen;

    public TopicEditorDialog()
    {
        InitializeComponent();
        Resized += (_, _) => FitWorkArea();
        PositionChanged += (_, _) => FitWorkArea();
        Closed += (_, _) => isOpen = false;
    }

    void OnOpened(object? sender, System.EventArgs e)
    {
        isOpen = true;
        FitWorkArea();
        (NameField.IsEnabled ? (Control)NameField : MaxSizeField).Focus(NavigationMethod.Tab);
    }

    void FitWorkArea()
    {
        if (!isOpen) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var frame = FrameSize ?? ClientSize;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        MaxHeight = System.Math.Min(MaxHeight, System.Math.Max(0, area.Height / scale - (frame.Height - ClientSize.Height)));
        MaxWidth = System.Math.Min(MaxWidth, System.Math.Max(0, area.Width / scale - (frame.Width - ClientSize.Width)));
        var x = System.Math.Clamp(Position.X, area.X, System.Math.Max(area.X, area.Right - (int)System.Math.Ceiling(frame.Width * scale)));
        var y = System.Math.Clamp(Position.Y, area.Y, System.Math.Max(area.Y, area.Bottom - (int)System.Math.Ceiling(frame.Height * scale)));
        if (Position.X != x || Position.Y != y) Position = new PixelPoint(x, y);
    }

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TopicEditorViewModel viewModel) return;
        var field = viewModel.RevealInvalidField();
        if (field is null)
        {
            Close(viewModel.ToDefinition());
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            var target = this.FindControl<Control>(field == nameof(TopicEditorViewModel.Name) ? "NameField" : field);
            target?.Focus(NavigationMethod.Tab);
            target?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
}
