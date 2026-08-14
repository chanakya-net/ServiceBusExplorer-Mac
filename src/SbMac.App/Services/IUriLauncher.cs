using System.Diagnostics;

namespace SbMac.App.Services;

public interface IUriLauncher
{
    Task OpenAsync(Uri uri);
}

public sealed class SystemUriLauncher : IUriLauncher
{
    public Task OpenAsync(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Opening release notes is best-effort and must not interrupt the app.
        }

        return Task.CompletedTask;
    }
}
