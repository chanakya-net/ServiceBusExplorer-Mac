using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using SbMac.App.ViewModels.Dialogs;

namespace SbMac.App.Views.Dialogs;

public partial class SendMessageDialog : Window
{
    bool isOpen;

    public SendMessageDialog()
    {
        InitializeComponent();
        Resized += (_, _) => FitWorkArea();
        PositionChanged += (_, _) => FitWorkArea();
        Closed += (_, _) => isOpen = false;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        isOpen = true;
        FitWorkArea();
    }

    void FitWorkArea()
    {
        if (!isOpen) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var frame = FrameSize ?? ClientSize;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        MaxHeight = Math.Min(MaxHeight, Math.Max(0, area.Height / scale - (frame.Height - ClientSize.Height)));
        MaxWidth = Math.Min(MaxWidth, Math.Max(0, area.Width / scale - (frame.Width - ClientSize.Width)));
        var x = Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(frame.Width * scale)));
        var y = Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(frame.Height * scale)));
        if (Position.X != x || Position.Y != y) Position = new PixelPoint(x, y);
    }

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
            return;
        }

        var target = viewModel.InvalidField switch
        {
            SendValidationField.Copies => (Control)CopiesInput,
            SendValidationField.Schedule => ScheduleToggle,
            _ => BodyInput
        };
        Dispatcher.UIThread.Post(() =>
        {
            target.BringIntoView();
            target.Focus();
        }, DispatcherPriority.Loaded);
    }
}
