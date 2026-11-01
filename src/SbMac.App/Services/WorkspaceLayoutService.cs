using Avalonia;
using SbMac.App.Models;

namespace SbMac.App.Services;

/// <summary>A screen working area in physical pixels plus that screen's own scaling factor.</summary>
public sealed record ScreenWorkArea(WindowBounds Pixels, double Scaling, bool IsPrimary)
{
    internal bool IsUsable => Scaling is > 0 and <= 16 && double.IsFinite(Scaling) &&
        double.IsFinite(Pixels.X) && double.IsFinite(Pixels.Y) &&
        Pixels.Width > 0 && Pixels.Height > 0 && double.IsFinite(Pixels.Width) && double.IsFinite(Pixels.Height);

    internal WindowBounds Logical => new(Pixels.X / Scaling, Pixels.Y / Scaling, Pixels.Width / Scaling, Pixels.Height / Scaling);
}

public sealed record WindowPlacement(WindowBounds Logical, PixelPoint Position);

public static class WorkspaceLayoutService
{
    /// <summary>
    /// Recover saved logical bounds into the best available work area. Each screen's area is converted with
    /// its own scaling; the final pixel position uses the chosen screen's scaling. Primary wins ties.
    /// </summary>
    public static WindowPlacement Restore(WindowBounds saved, IReadOnlyList<ScreenWorkArea> screens)
    {
        var usable = screens.Where(screen => screen.IsUsable).OrderByDescending(screen => screen.IsPrimary).ToArray();
        if (usable.Length == 0)
            return new(saved, new((int)Math.Round(saved.X), (int)Math.Round(saved.Y)));
        var bounds = saved.Constrain(usable.Select(screen => screen.Logical).ToArray());
        var target = usable.FirstOrDefault(screen => Contains(screen.Logical, bounds)) ?? usable[0];
        return new(bounds, new((int)Math.Round(bounds.X * target.Scaling), (int)Math.Round(bounds.Y * target.Scaling)));
    }

    static bool Contains(WindowBounds area, WindowBounds bounds) =>
        bounds.X >= area.X && bounds.Y >= area.Y && bounds.X < area.X + area.Width && bounds.Y < area.Y + area.Height;
}
