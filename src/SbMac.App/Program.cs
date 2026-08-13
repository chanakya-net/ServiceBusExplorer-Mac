using Avalonia;

namespace SbMac.App;

static class Program
{
    // Avalonia needs a synchronous entry point before any SynchronizationContext exists,
    // so this must stay [STAThread] and must not become async.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>Also used by the Avalonia XAML previewer, which calls it by convention.</summary>
    /// <remarks>
    /// The backend is named explicitly rather than found by UsePlatformDetect, because
    /// that helper lives in the Avalonia.Desktop metapackage this project deliberately
    /// doesn't reference — see the note in SbMac.App.csproj.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseAvaloniaNative()
            .UseSkia()
            // Avalonia 12 no longer wires text shaping up implicitly; omitting this throws
            // "No text shaping system configured" during startup.
            .UseHarfBuzz()
            .WithInterFont()
            .LogToTrace();
}
