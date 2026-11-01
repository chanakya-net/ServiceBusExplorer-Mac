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
    public const int SchemaVersion = 2;

    public WorkspacePreferences Workspace { get; init; } = new(null, WorkspaceWindowState.Normal);

    public static AppPreferences Default { get; } = new(AppTheme.System);
}

/// <summary>Client rectangle in desktop logical units, independent of render scaling.</summary>
public sealed record WindowBounds(double X, double Y, double Width, double Height)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Width) &&
        double.IsFinite(Height) && Math.Abs(X) <= 1_000_000 && Math.Abs(Y) <= 1_000_000 &&
        Width is >= 960 and <= 100_000 && Height is >= 600 and <= 100_000;

    /// <summary>Fit client size plus title bar into the best available desktop work area.</summary>
    internal WindowBounds Constrain(IReadOnlyList<WindowBounds> workAreas)
    {
        if (workAreas.Count == 0) return this;
        var area = workAreas.OrderByDescending(IntersectionArea).First();
        var width = Math.Clamp(Width, 960, Math.Max(960, area.Width));
        var height = Math.Clamp(Height, 600, Math.Max(600, area.Height - 32));
        return new(Math.Clamp(X, area.X, Math.Max(area.X, area.X + area.Width - width)),
            Math.Clamp(Y, area.Y, Math.Max(area.Y, area.Y + area.Height - height - 32)), width, height);
    }

    double IntersectionArea(WindowBounds area) =>
        Math.Max(0, Math.Min(X + Width, area.X + area.Width) - Math.Max(X, area.X)) *
        Math.Max(0, Math.Min(Y + Height, area.Y + area.Height) - Math.Max(Y, area.Y));
}

public enum WorkspaceWindowState { Normal, Maximized }

public sealed record WorkspacePreferences(WindowBounds? WindowBounds, WorkspaceWindowState WindowState)
{
    internal WorkspacePreferences Normalize() => WindowBounds is { IsValid: true }
        ? this with { WindowState = Enum.IsDefined(WindowState) ? WindowState : WorkspaceWindowState.Normal }
        : new(null, WorkspaceWindowState.Normal);
}
