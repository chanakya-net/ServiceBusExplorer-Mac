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
    public const int SchemaVersion = 1;

    public static AppPreferences Default { get; } = new(AppTheme.System);
}
