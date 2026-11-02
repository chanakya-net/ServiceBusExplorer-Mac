using Avalonia.Controls;
using Avalonia.Threading;
using SbMac.App.Models;
using SbMac.App.Services;

namespace SbMac.App.Views;

public partial class MainWindow
{
    void OnLayoutReset(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) ResetWorkspaceLayout();
        else Dispatcher.UIThread.Post(ResetWorkspaceLayout);
    }

    void ResetWorkspaceLayout()
    {
        resettingWorkspace = true;
        try
        {
            WindowState = WindowState.Normal;
            var screens = Screens.All.Select(screen => new ScreenWorkArea(
                new(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height),
                screen.Scaling, screen.IsPrimary)).ToArray();
            var placement = WorkspaceLayoutService.Restore(
                DesktopBounds(Position, defaultClientSize, DesktopScaling), screens);
            Width = placement.Logical.Width;
            Height = placement.Logical.Height;
            Position = placement.Position;
            tableHeight = new GridLength(1.5, GridUnitType.Star);
            inspectorHeight = new GridLength(1, GridUnitType.Star);
            ApplySavedPanels(WorkspacePreferences.Default);
            lastWorkspace = WorkspacePreferences.Default;
        }
        finally
        {
            // Resize captures already posted during the reset must drain first.
            Dispatcher.UIThread.Post(() => resettingWorkspace = false, DispatcherPriority.Background);
        }
    }
}
