using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SbMac.App.Converters;

/// <summary>
/// Resolves a brush name from a view model into the brush declared in Styles/Theme.axaml,
/// so view models can pick a colour by token without referencing drawing types. Mirrors
/// <see cref="IconLookupConverter"/>, which does the same for geometry.
/// </summary>
public sealed class BrushLookupConverter : IValueConverter
{
    public static BrushLookupConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key) || Application.Current is null)
        {
            return null;
        }

        // Unlike geometry, these live in the theme dictionaries, so the lookup has to be
        // told which variant is on screen.
        return Application.Current.Resources
            .TryGetResource(key, Application.Current.ActualThemeVariant, out var resource)
            ? resource as IBrush
            : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
