using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using SbMac.App.Services;
using SbMac.App.ViewModels;
using SbMac.App.Views;

namespace SbMac.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avalonia 11 registered a DataAnnotations validator by default, which
            // duplicated every error the view models raise themselves, so it had to be
            // removed here. Avalonia 12 no longer registers it — and made BindingPlugins
            // internal — so there is nothing left to undo.
            var viewModel = new MainWindowViewModel(UpdateChecker.CreateDefault());
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Close cleanly so in-flight sessions get disposed rather than torn down.
            desktop.ShutdownRequested += (_, _) => viewModel.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
