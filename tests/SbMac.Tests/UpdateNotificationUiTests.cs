using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;

using SbMac.App.Services;
using SbMac.App.Views;
using SbMac.App.Views.Dialogs;

using Xunit;

namespace SbMac.Tests;

public sealed class UpdateNotificationUiTests
{
    static readonly UpdateInfo Update = new(
        new Version(1, 3, 2),
        new Version(1, 4, 0),
        new Uri("https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/v1.4.0"));

    [AvaloniaFact]
    public async Task LaterClosesPromptWithoutOpeningBrowser()
    {
        var launcher = new RecordingUriLauncher();
        var owner = new MainWindow(launcher);
        owner.Show();

        var prompt = owner.ShowUpdateAvailableAsync(Update);
        var dialog = Assert.IsType<MessageDialog>(Assert.Single(owner.OwnedWindows));
        var buttons = GetButtons(dialog);

        Assert.Equal("Later", buttons[0].Content);
        Assert.Equal("View Release", buttons[1].Content);
        Assert.Contains("Version 1.4.0 is available", GetMessage(dialog));
        Assert.Contains("using 1.3.2", GetMessage(dialog));
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await prompt;
        Assert.Null(launcher.OpenedUri);
    }

    [AvaloniaFact]
    public async Task ViewReleaseOpensValidatedReleaseUri()
    {
        var launcher = new RecordingUriLauncher();
        var owner = new MainWindow(launcher);
        owner.Show();

        var prompt = owner.ShowUpdateAvailableAsync(Update);
        var dialog = Assert.IsType<MessageDialog>(Assert.Single(owner.OwnedWindows));
        var buttons = GetButtons(dialog);

        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await prompt;
        Assert.Equal(Update.ReleaseUri, launcher.OpenedUri);
    }

    [AvaloniaFact]
    public async Task BrowserLauncherFailureIsSilent()
    {
        var owner = new MainWindow(new ThrowingUriLauncher());
        owner.Show();

        var prompt = owner.ShowUpdateAvailableAsync(Update);
        var dialog = Assert.IsType<MessageDialog>(Assert.Single(owner.OwnedWindows));
        GetButtons(dialog)[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await prompt;
    }

    static IReadOnlyList<Button> GetButtons(MessageDialog dialog)
    {
        var root = Assert.IsType<StackPanel>(dialog.Content);
        return Assert.IsType<StackPanel>(root.Children[2]).Children.Cast<Button>().ToList();
    }

    static string GetMessage(MessageDialog dialog)
    {
        var root = Assert.IsType<StackPanel>(dialog.Content);
        var scroller = Assert.IsType<ScrollViewer>(root.Children[1]);
        return Assert.IsType<TextBlock>(scroller.Content).Text!;
    }

    sealed class RecordingUriLauncher : IUriLauncher
    {
        public Uri? OpenedUri { get; private set; }

        public Task OpenAsync(Uri uri)
        {
            OpenedUri = uri;
            return Task.CompletedTask;
        }
    }

    sealed class ThrowingUriLauncher : IUriLauncher
    {
        public Task OpenAsync(Uri uri) => throw new InvalidOperationException("browser unavailable");
    }
}
