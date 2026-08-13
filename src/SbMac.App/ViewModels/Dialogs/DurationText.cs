using System.Globalization;
using System.Xml;

namespace SbMac.App.ViewModels.Dialogs;

/// <summary>
/// Converts between the ISO 8601 durations stored in entity definitions and the
/// <c>d.hh:mm:ss</c> form that's readable in a text box.
/// </summary>
public static class DurationText
{
    /// <summary>Definition value → text box. Blank means "leave the service default".</summary>
    public static string ToDisplay(string? definitionValue)
    {
        var span = Parse(definitionValue);
        return span is null ? string.Empty : Format(span.Value);
    }

    /// <summary>Text box → definition value. Blank round-trips to null.</summary>
    public static string? ToDefinition(string? displayValue)
    {
        var span = Parse(displayValue);
        return span is null ? null : XmlConvert.ToString(span.Value);
    }

    /// <summary>True when the text is blank or parses as a duration — used to gate the Save button.</summary>
    public static bool IsValid(string? displayValue) =>
        string.IsNullOrWhiteSpace(displayValue) || Parse(displayValue) is not null;

    /// <summary>Accepts both ISO 8601 (<c>PT30S</c>) and .NET (<c>00:00:30</c>) forms.</summary>
    public static TimeSpan? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        if (trimmed.StartsWith('P') || trimmed.StartsWith("-P", StringComparison.Ordinal))
        {
            try
            {
                return XmlConvert.ToTimeSpan(trimmed);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return TimeSpan.TryParse(trimmed, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    static string Format(TimeSpan value) =>
        value.Days > 0
            ? value.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture)
            : value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
}
