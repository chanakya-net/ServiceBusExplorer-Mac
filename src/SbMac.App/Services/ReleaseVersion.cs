namespace SbMac.App.Services;

static class ReleaseVersion
{
    public static Version? Parse(string? text, bool allowLeadingV = false)
    {
        var value = text;
        if (allowLeadingV && value is { Length: > 1 } && value[0] == 'v')
        {
            value = value[1..];
        }

        var parts = value?.Split('.');
        if (parts is not { Length: 3 } ||
            !parts.All(IsAsciiDigits) ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch))
        {
            return null;
        }

        return new Version(major, minor, patch);
    }

    static bool IsAsciiDigits(string component) =>
        component.Length > 0 && component.All(character => character is >= '0' and <= '9');
}
