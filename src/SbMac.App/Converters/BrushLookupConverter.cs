using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace SbMac.App.Converters;

/// <summary>
/// Resolves a brush name from a view model into the brush declared in Styles/Theme.axaml,
/// so view models can pick a colour by token without referencing drawing types. Mirrors
/// <see cref="IconLookupConverter"/>, which does the same for geometry.
/// </summary>
/// <remarks>
/// Bound as a multi-binding of the brush key and the target's <c>ActualThemeVariant</c>.
/// Unlike geometry, these brushes live in the theme dictionaries, and a plain lookup is
/// not re-run when the appearance changes — taking the variant as an input is what makes
/// an already-visible bar recolour when the user switches between Light and Dark.
/// </remarks>
public sealed class BrushLookupConverter : IMultiValueConverter
{
    public static BrushLookupConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0 || values[0] is not string key || string.IsNullOrEmpty(key) ||
            Application.Current is not { } application)
        {
            return null;
        }

        var variant = values.Count > 1 && values[1] is ThemeVariant actual
            ? actual
            : application.ActualThemeVariant;

        return application.Resources.TryGetResource(key, variant, out var resource)
            ? resource as IBrush
            : null;
    }
}
