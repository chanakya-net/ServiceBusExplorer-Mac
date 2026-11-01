using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SbMac.App.Models;
using SbMac.App.Services;

namespace SbMac.App.Views;

public partial class MainWindow
{
    AppPreferencesService? workspacePreferences;
    bool workspaceOpened;
    bool captureScheduled;
    WorkspacePreferences? lastWorkspace;

    internal void AttachWorkspacePreferences(AppPreferencesService preferences)
    {
        workspacePreferences = preferences;
        lastWorkspace = preferences.Current.Workspace;
        if (lastWorkspace.WindowBounds is { } saved)
        {
            var workAreas = Screens.All.OrderByDescending(screen => screen.IsPrimary).Select(screen =>
                new WindowBounds(screen.WorkingArea.X / DesktopScaling, screen.WorkingArea.Y / DesktopScaling,
                    screen.WorkingArea.Width / DesktopScaling, screen.WorkingArea.Height / DesktopScaling)).ToArray();
            var bounds = saved.Constrain(workAreas);
            Width = bounds.Width;
            Height = bounds.Height;
            Position = new PixelPoint((int)(bounds.X * DesktopScaling), (int)(bounds.Y * DesktopScaling));
        }
        if (lastWorkspace.WindowState == WorkspaceWindowState.Maximized)
            WindowState = WindowState.Maximized;
        Opened += OnWorkspaceOpened;
        PositionChanged += OnWorkspacePositionChanged;
        PropertyChanged += OnWorkspacePropertyChanged;
        Closing += OnWorkspaceClosing;
        Closed += OnWorkspaceClosed;
    }

    void OnWorkspaceOpened(object? sender, EventArgs e)
    {
        workspaceOpened = true;
        if (WindowState == WindowState.Normal)
            lastWorkspace = new(DesktopBounds(Position, ClientSize, DesktopScaling), WorkspaceWindowState.Normal);
    }

    void OnWorkspacePositionChanged(object? sender, PixelPointEventArgs e) => ScheduleWorkspaceCapture();

    void OnWorkspacePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty && workspaceOpened && WindowState == WindowState.Maximized)
            SaveWorkspace(lastWorkspace! with { WindowState = WorkspaceWindowState.Maximized });
        if (e.Property == ClientSizeProperty || e.Property == WindowStateProperty)
            ScheduleWorkspaceCapture();
    }

    void ScheduleWorkspaceCapture()
    {
        if (!workspaceOpened || captureScheduled) return;
        captureScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            captureScheduled = false;
            CaptureWorkspace();
        }, DispatcherPriority.Background);
    }

    internal void CaptureWorkspace()
    {
        if (!workspaceOpened || workspacePreferences is null || WindowState != WindowState.Normal) return;
        SaveWorkspace(new(DesktopBounds(Position, ClientSize, DesktopScaling), WorkspaceWindowState.Normal));
    }

    internal static WindowBounds DesktopBounds(PixelPoint position, Size clientSize, double desktopScaling) =>
        new(position.X / desktopScaling, position.Y / desktopScaling, clientSize.Width, clientSize.Height);

    void SaveWorkspace(WorkspacePreferences snapshot)
    {
        if (snapshot.WindowBounds is not { IsValid: true } || snapshot == lastWorkspace) return;
        lastWorkspace = snapshot;
        workspacePreferences!.SetWorkspace(snapshot);
    }

    void OnWorkspaceClosing(object? sender, WindowClosingEventArgs e) => CaptureWorkspace();

    void OnWorkspaceClosed(object? sender, EventArgs e)
    {
        workspaceOpened = false;
        Opened -= OnWorkspaceOpened;
        PositionChanged -= OnWorkspacePositionChanged;
        PropertyChanged -= OnWorkspacePropertyChanged;
        Closing -= OnWorkspaceClosing;
        Closed -= OnWorkspaceClosed;
    }
}
