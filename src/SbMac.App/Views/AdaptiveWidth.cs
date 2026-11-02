using Avalonia;
using Avalonia.Controls;

namespace SbMac.App.Views;

/// <summary>
/// Measures how much width a row of controls needs in its full form, so layouts collapse
/// into overflow menus from real content width rather than a fixed window-size guess.
/// </summary>
internal static class AdaptiveWidth
{
    /// <summary>Sum of unconstrained desired widths (margins included) of the given controls.</summary>
    public static double Required(IEnumerable<Control> controls)
    {
        var total = 0.0;
        foreach (var control in controls)
        {
            control.Measure(Size.Infinity);
            total += control.DesiredSize.Width;
        }
        return total;
    }

    /// <summary>True when the full-form content no longer fits the available width.</summary>
    public static bool NeedsCompaction(IEnumerable<Control> controls, double available) =>
        available > 0 && Required(controls) > available;
}
