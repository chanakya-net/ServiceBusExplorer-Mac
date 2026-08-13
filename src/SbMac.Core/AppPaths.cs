namespace SbMac.Core;

/// <summary>
/// Where SB-Mac keeps its files. Follows the macOS convention of
/// <c>~/Library/Application Support/&lt;app&gt;</c> rather than a dotfile in $HOME.
/// </summary>
public static class AppPaths
{
    const string AppFolderName = "SB-Mac";

    /// <summary>Root config directory. Created on first access.</summary>
    public static string ConfigDirectory
    {
        get
        {
            // On macOS, SpecialFolder.ApplicationData resolves to ~/.config under .NET,
            // so build the Application Support path explicitly to match platform convention.
            var root = OperatingSystem.IsMacOS()
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support")
                : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var directory = Path.Combine(root, AppFolderName);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    /// <summary>Saved namespace connections (no secrets).</summary>
    public static string ConnectionsFile => Path.Combine(ConfigDirectory, "connections.json");

    /// <summary>User preferences.</summary>
    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");

    /// <summary>Fallback secret file, used only when the keychain is unavailable.</summary>
    public static string FallbackSecretsFile => Path.Combine(ConfigDirectory, "secrets.json");

    /// <summary>Default directory offered when exporting entity definitions or messages.</summary>
    public static string DefaultExportDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
}
