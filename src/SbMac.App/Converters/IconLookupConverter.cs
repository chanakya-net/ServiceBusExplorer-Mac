using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SbMac.App.Converters;

/// <summary>
/// Resolves an icon name from a view model into the <see cref="StreamGeometry"/> declared
/// in Styles/Icons.axaml, so view models name their icon without referencing drawing types.
/// </summary>
public sealed class IconLookupConverter : IValueConverter
{
    public static IconLookupConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrEmpty(key))
        {
            return null;
        }

        if (Application.Current is null)
        {
            return null;
        }

        // The theme variant matters for colour resources; geometry is variant-independent,
        // so the current variant is fine to pass through.
        return Application.Current.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var resource)
            ? resource as StreamGeometry
            : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
