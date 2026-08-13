using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;

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
            // Avalonia's DataAnnotations validator duplicates every validation error
            // raised by the view models' own INotifyDataErrorInfo implementation.
            DisableDuplicateValidation();

            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow { DataContext = viewModel };

            // Close cleanly so in-flight sessions get disposed rather than torn down.
            desktop.ShutdownRequested += (_, _) => viewModel.Shutdown();
        }

        base.OnFrameworkInitializationCompleted();
    }

    static void DisableDuplicateValidation()
    {
        foreach (var plugin in BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray())
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
