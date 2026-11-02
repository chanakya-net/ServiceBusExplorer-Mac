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
    bool resettingWorkspace;
    Size defaultClientSize;
    bool captureScheduled;
    WorkspacePreferences? lastWorkspace;

    internal void AttachWorkspacePreferences(AppPreferencesService preferences)
    {
        defaultClientSize = new Size(Width, Height);
        workspacePreferences = preferences;
        preferences.LayoutReset += OnLayoutReset;
        lastWorkspace = preferences.Current.Workspace;
        if (lastWorkspace.WindowBounds is { } saved)
        {
            var screens = Screens.All.Select(screen => new ScreenWorkArea(
                new(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height),
                screen.Scaling, screen.IsPrimary)).ToArray();
            var placement = WorkspaceLayoutService.Restore(saved, screens);
            Width = placement.Logical.Width;
            Height = placement.Logical.Height;
            Position = placement.Position;
        }
        if (lastWorkspace.WindowState == WorkspaceWindowState.Maximized)
            WindowState = WindowState.Maximized;
        ApplySavedPanels(lastWorkspace!);
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
            lastWorkspace = Snapshot(DesktopBounds(Position, ClientSize, DesktopScaling), WorkspaceWindowState.Normal);
    }

    void OnWorkspacePositionChanged(object? sender, PixelPointEventArgs e) => ScheduleWorkspaceCapture();

    void OnWorkspacePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!resettingWorkspace && e.Property == WindowStateProperty && workspaceOpened && WindowState == WindowState.Maximized)
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
        if (resettingWorkspace || !workspaceOpened || workspacePreferences is null || WindowState != WindowState.Normal) return;
        SaveWorkspace(Snapshot(DesktopBounds(Position, ClientSize, DesktopScaling), WorkspaceWindowState.Normal));
    }

    internal static WindowBounds DesktopBounds(PixelPoint position, Size clientSize, double desktopScaling) =>
        new(position.X / desktopScaling, position.Y / desktopScaling, clientSize.Width, clientSize.Height);

    /// <summary>The last known preferences with only the window placement replaced, so panel fields carry over.</summary>
    WorkspacePreferences Snapshot(WindowBounds? bounds, WorkspaceWindowState state) =>
        (lastWorkspace ?? WorkspacePreferences.Default) with { WindowBounds = bounds, WindowState = state };

    void SaveWorkspace(WorkspacePreferences snapshot)
    {
        if (snapshot.WindowBounds is { IsValid: false } || snapshot == lastWorkspace) return;
        lastWorkspace = snapshot;
        workspacePreferences!.SetWorkspace(snapshot);
    }

    void OnWorkspaceClosing(object? sender, WindowClosingEventArgs e) => CaptureWorkspace();

    void OnWorkspaceClosed(object? sender, EventArgs e)
    {
        workspaceOpened = false;
        if (workspacePreferences is not null) workspacePreferences.LayoutReset -= OnLayoutReset;
        Opened -= OnWorkspaceOpened;
        PositionChanged -= OnWorkspacePositionChanged;
        PropertyChanged -= OnWorkspacePropertyChanged;
        Closing -= OnWorkspaceClosing;
        Closed -= OnWorkspaceClosed;
    }
}
