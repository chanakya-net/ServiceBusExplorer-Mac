namespace SbMac.App.Models;

/// <summary>Which appearance the app uses. System follows the macOS setting.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark
}

/// <summary>
/// The preferences SB-Mac persists in its settings file. Other consumers (the update
/// checker, future features) store their own keys in the same document, so this type
/// only describes the fields it owns.
/// </summary>
public sealed record AppPreferences(AppTheme Theme)
{
    /// <summary>Settings schema written alongside these preferences.</summary>
    public const int SchemaVersion = 3;

    public bool WordWrap { get; init; }

    public WorkspacePreferences Workspace { get; init; } = new(null, WorkspaceWindowState.Normal);

    public Dictionary<string, IReadOnlyList<MessageColumnState>> MessageTables { get; init; } = EmptyMessageTables;

    static readonly Dictionary<string, IReadOnlyList<MessageColumnState>> EmptyMessageTables = new();

    public static AppPreferences Default { get; } = new(AppTheme.System);
}

/// <summary>Client rectangle in desktop logical units, independent of render scaling.</summary>
public sealed record WindowBounds(double X, double Y, double Width, double Height)
{
    // MainWindow.axaml uses these same minimum client dimensions.
    public const double MinimumWidth = 960;
    public const double MinimumHeight = 600;
    public const double TitleBarAllowance = 32;

    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) &&
        double.IsFinite(Height) && Math.Abs(X) <= 1_000_000 && Math.Abs(Y) <= 1_000_000 &&
        Width is >= MinimumWidth and <= 100_000 && Height is >= MinimumHeight and <= 100_000;

    /// <summary>Fit client size plus title bar into the best available desktop work area.</summary>
    internal WindowBounds Constrain(IReadOnlyList<WindowBounds> workAreas)
    {
        if (workAreas.Count == 0) return this;
        var area = workAreas.OrderByDescending(IntersectionArea).First();
        var width = Math.Clamp(Width, MinimumWidth, Math.Max(MinimumWidth, area.Width));
        var height = Math.Clamp(Height, MinimumHeight, Math.Max(MinimumHeight, area.Height - TitleBarAllowance));
        return new(Math.Clamp(X, area.X, Math.Max(area.X, area.X + area.Width - width)),
            Math.Clamp(Y, area.Y, Math.Max(area.Y, area.Y + area.Height - height - TitleBarAllowance)), width, height);
    }

    internal double IntersectionArea(WindowBounds area) =>
        Math.Max(0, Math.Min(X + Width, area.X + area.Width) - Math.Max(X, area.X)) *
        Math.Max(0, Math.Min(Y + Height, area.Y + area.Height) - Math.Max(Y, area.Y));
}

public enum WorkspaceWindowState { Normal, Maximized }

/// <summary>
/// Window placement plus the panel layout. Panel fields are independent of window
/// placement: an unusable window rectangle drops only the rectangle and its state.
/// </summary>
public sealed record WorkspacePreferences(
    WindowBounds? WindowBounds,
    WorkspaceWindowState WindowState,
    double SidebarWidth = WorkspacePreferences.DefaultSidebarWidth,
    double? DetailsHeight = null,
    bool DetailsCollapsed = false,
    bool ActivityCollapsed = true)
{
    public const double DefaultSidebarWidth = 280;
    public const double MinSidebarWidth = 200;
    public const double MaxSidebarWidth = 560;
    public const double MinDetailsHeight = 120;
    public const double MaxDetailsHeight = 4000;

    /// <summary>Reset target: no saved placement, default panels (details open, activity collapsed).</summary>
    public static WorkspacePreferences Default { get; } = new(null, WorkspaceWindowState.Normal);

    /// <summary>Drops unusable values and clamps oversized panel dimensions into their ranges.</summary>
    internal WorkspacePreferences Normalize()
    {
        var validBounds = WindowBounds is { IsValid: true };
        return this with
        {
            WindowBounds = validBounds ? WindowBounds : null,
            WindowState = validBounds && Enum.IsDefined(WindowState) ? WindowState : WorkspaceWindowState.Normal,
            SidebarWidth = double.IsFinite(SidebarWidth)
                ? Math.Clamp(SidebarWidth, MinSidebarWidth, MaxSidebarWidth)
                : DefaultSidebarWidth,
            DetailsHeight = DetailsHeight is { } height && double.IsFinite(height)
                ? Math.Clamp(height, MinDetailsHeight, MaxDetailsHeight)
                : null
        };
    }
}

public sealed record MessageColumnState(string Id, bool Visible, int? DisplayIndex = null, double? Width = null)
{
    // Logical pixels; grid-specific minimum/maximum constraints apply afterwards.
    public static double? NormalizeWidth(double? width) => width is { } value && double.IsFinite(value) && value > 0
        ? Math.Clamp(value, 20, 10000) : null;

    public MessageColumnState Normalize() => this with
    {
        DisplayIndex = DisplayIndex is >= 0 ? DisplayIndex : null,
        Width = NormalizeWidth(Width)
    };
}
