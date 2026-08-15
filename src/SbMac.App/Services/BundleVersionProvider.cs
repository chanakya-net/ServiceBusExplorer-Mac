using System.Xml.Linq;

namespace SbMac.App.Services;

public sealed class BundleVersionProvider(string? executableDirectory = null) : IAppVersionProvider
{
    readonly string executableDirectory = executableDirectory ?? AppContext.BaseDirectory;

    public Version? GetCurrentVersion()
    {
        try
        {
            var infoPlist = Path.GetFullPath(Path.Combine(executableDirectory, "..", "Info.plist"));
            var elements = XDocument.Load(infoPlist).Root?.Element("dict")?.Elements().ToList();
            if (elements is null)
            {
                return null;
            }

            for (var index = 0; index + 1 < elements.Count; index++)
            {
                if (elements[index].Name == "key" &&
                    elements[index].Value == "CFBundleShortVersionString" &&
                    elements[index + 1].Name == "string")
                {
                    return ReleaseVersion.Parse(elements[index + 1].Value);
                }
            }
        }
        catch
        {
            // An unbundled or unreadable build is not eligible for update checks.
        }

        return null;
    }
}
