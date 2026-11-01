using Avalonia;

using SbMac.App;
using SbMac.App.Services;
using SbMac.App.ViewModels;
using SbMac.App.Views;
using SbMac.Core.Connections;

namespace SbMac.Preview;

/// <summary>
/// Dev-only: runs the real app — native macOS backend, real application menu, real
/// Cmd+, — against caller-owned temporary state, for checks headless rendering cannot
/// make. It never reads the user's settings, connections or Keychain, has no update
/// checker, and refuses to connect to Azure.
/// </summary>
static class NativePreview
{
    /// <summary>Accepts only a directory inside the OS temporary root, creating it if needed.</summary>
    public static string ValidateStateDirectory(string stateDirectory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateDirectory));
        var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        if (!full.StartsWith(tempRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"--state-dir must be inside {tempRoot}.");
        }

        Directory.CreateDirectory(full);
        return full;
    }

    public static void Run(string stateDirectory, int? width, int? height)
    {
        var preferences = new AppPreferencesService(
            new JsonSettingsStore(Path.Combine(stateDirectory, "settings.json")));
        var connections = new ConnectionStore(
            Path.Combine(stateDirectory, "connections.json"),
            new FileSecretStore(Path.Combine(stateDirectory, "secrets.json")));
        SeedConnections(connections, Path.Combine(stateDirectory, "connections.json"));

        var composition = new AppComposition(preferences, loaded =>
        {
            var viewModel = MainWindowViewModel.CreateIsolated(connections, RejectAzure, loaded);
            var window = new MainWindow { DataContext = viewModel };
            if (width is { } w) window.Width = w;
            if (height is { } h) window.Height = h;
            return window;
        });

        // Settings are loaded by the app itself, before the window exists; the saved
        // theme is never forced afterwards.
        AppBuilder.Configure(() => new App.App(composition))
            .UseAvaloniaNative()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .StartWithClassicDesktopLifetime([]);
    }

    static Task RejectAzure(SbMac.App.ViewModels.Tree.NamespaceNodeViewModel node, CancellationToken token) =>
        throw new InvalidOperationException("The native preview cannot connect to Azure.");

    static void SeedConnections(ConnectionStore connections, string file)
    {
        if (File.Exists(file))
        {
            return;
        }

        connections.SaveAsync(
        [
            new NamespaceConnection
            {
                Name = "contoso-fixture",
                AuthenticationMode = AuthenticationMode.EntraId,
                EntraCredentialKind = EntraCredentialKind.AzureCli,
                FullyQualifiedNamespace = "contoso-fixture.servicebus.windows.net"
            }
        ]).GetAwaiter().GetResult();
    }
}
