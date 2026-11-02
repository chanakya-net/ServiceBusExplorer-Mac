using Avalonia.Threading;

namespace SbMac.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    void OnLayoutReset(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) ResetMessageColumns();
        else Dispatcher.UIThread.Post(ResetMessageColumns);
    }

    void ResetMessageColumns()
    {
        sessionColumns.Clear();
        OnPropertyChanged(nameof(MessageColumns));
    }
}
