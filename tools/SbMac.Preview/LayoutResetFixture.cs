using Avalonia;
using Avalonia.Controls;
using SbMac.App;
using SbMac.App.Services;
using SbMac.App.ViewModels;
using SbMac.App.Views;
using SbMac.Core.Connections;

namespace SbMac.Preview;

/// <summary>Disposable synthetic workspace for reset screenshots; never uses personal state.</summary>
static class LayoutResetFixture
{
    public static MainWindow Build(string root, string theme, Action<Task> pump)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N")));
        var settings = Path.Combine(dir.FullName, "settings.json");
        File.WriteAllText(settings, """
            {"theme":"THEME","workspace":{"windowBounds":{"x":100,"y":80,"width":1120,"height":720},"sidebarWidth":400,"detailsHeight":300,"detailsCollapsed":true,"activityCollapsed":false},
             "messageTables":{"serviceBus":{"columns":[{"id":"sequence","visible":false,"width":180}]},"deadLetter":{"columns":[{"id":"reason","visible":false}]},"eventHubs":{"columns":[{"id":"offset","visible":false}]}}}
            """.Replace("THEME", theme));
        var preferences = new AppPreferencesService(new JsonSettingsStore(settings));
        var store = new ConnectionStore(Path.Combine(dir.FullName, "connections.json"),
            new FileSecretStore(Path.Combine(dir.FullName, "secrets.json")));
        pump(store.SaveAsync([new NamespaceConnection
        {
            Name = "contoso-fixture", AuthenticationMode = AuthenticationMode.EntraId,
            FullyQualifiedNamespace = "contoso-fixture.servicebus.windows.net"
        }]));
        var app = (App.App)Application.Current!;
        var window = app.CreateMainWindow(new AppComposition(preferences, loaded => new MainWindow
        {
            DataContext = MainWindowViewModel.CreateIsolated(store,
                (_, _) => throw new InvalidOperationException("Preview cannot connect to Azure."), loaded)
        }));
        var vm = (MainWindowViewModel)window.DataContext!;
        pump(vm.InitializeAsync());
        NativeTableLayoutFixture.Seed(window, vm);
        window.Tag = preferences;
        return window;
    }
}
