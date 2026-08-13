using Avalonia;
using Avalonia.Headless;

using SbMac.App;

[assembly: AvaloniaTestApplication(typeof(SbMac.Tests.TestAppBuilder))]

namespace SbMac.Tests;

/// <summary>
/// Boots the real <see cref="App"/> against Avalonia's headless platform, so UI tests
/// exercise the same styles, templates and compiled bindings the shipped app uses.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<global::SbMac.App.App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
