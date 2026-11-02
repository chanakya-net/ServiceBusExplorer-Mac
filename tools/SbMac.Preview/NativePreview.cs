using Avalonia;
using Avalonia.Controls;

using SbMac.App.ViewModels.Dialogs;
using SbMac.App.Views.Dialogs;
using SbMac.Core.Messaging;
using SbMac.Core.ImportExport;

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

    public static void Run(string stateDirectory, int? width, int? height, string? screen = null)
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
            if (screen == "table-layout")
            {
                loaded.LayoutReset += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    Console.WriteLine("WORKSPACE_RESET=" + System.Text.Json.JsonSerializer.Serialize(new
                    {
                        width = window.Width, height = window.Height, state = window.WindowState.ToString(),
                        sidebar = window.FindControl<Grid>("MainSplitLayout")!.ColumnDefinitions[0].Width.Value,
                        inspector = viewModel.IsInspectorVisible, activity = viewModel.IsActivityExpanded
                    })), Avalonia.Threading.DispatcherPriority.Background);
                window.Opened += async (_, _) =>
                {
                    await viewModel.InitializeAsync();
                    NativeTableLayoutFixture.Seed(window, viewModel);
                };
            }
            else if (screen is "resubmit" or "resubmit-long")
                window.Opened += async (_, _) => await ShowResubmitFixture(window, screen);
            else if (screen is not null) window.Opened += (_, _) => (screen == "rule" ? (Window)BuildRuleFixture() : screen == "topic" ? BuildTopicFixture() : screen == "subscription" ? BuildSubscriptionFixture() : BuildSendFixture(screen)).Show();
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

    // Uses only synthetic data; when available, the secondary screen tests real screen bounds.
    static async Task ShowResubmitFixture(Window owner, string screen)
    {
        if (screen == "resubmit-long")
        {
            var secondary = owner.Screens.All.FirstOrDefault(s => !s.IsPrimary);
            if (secondary is not null)
            {
                owner.Width = 500;
                owner.Height = 250;
                owner.Position = secondary.WorkingArea.Position;
                await Task.Delay(500); // Allow the native window move to settle before centering the dialog.
                Console.WriteLine($"FIXTURE_SCREEN={secondary.WorkingArea}, scale={secondary.Scaling} OWNER={owner.Position}");
            }
        }
        var target = screen == "resubmit" ? "orders" : string.Concat(Enumerable.Repeat("very-long-dead-letter-target-", 40));
        var pending = ResubmitDialog.ShowAsync(owner, 12, target);
        var dialog = owner.OwnedWindows.Single();
        Console.WriteLine($"DIALOG_SCREEN={dialog.Screens.ScreenFromWindow(dialog)?.WorkingArea} POSITION={dialog.Position}");
        var result = await pending;
        Console.WriteLine($"RESUBMIT_RESULT={result?.ToString() ?? "Cancel"}");
        owner.Close();
    }

    /// <summary>Standalone deterministic Send dialog, so native keyboard checks reach it without Azure.</summary>
    static SendMessageDialog BuildSendFixture(string screen)
    {
        var seed = screen == "send" ? new MessageRecord
        {
            Body = BinaryData.FromString("{\n  \"orderId\": 40921\n}"), SourceEntityPath = "orders",
            Subject = "order-created", ContentType = "application/json",
            ApplicationProperties = new Dictionary<string, object?> { ["region"] = "emea" }
        } : null;
        var request = new SendMessageRequest("orders", seed, seed is null ? null : "Editing a received message");
        var viewModel = new SendMessageViewModel(request);
        if (screen == "send-delivery")
        {
            viewModel.Body = "Order {n}";
            viewModel.IsDeliveryExpanded = true;
            viewModel.Copies = 3;
        }

        return new SendMessageDialog { DataContext = viewModel };
    }

    static RuleEditorDialog BuildRuleFixture() => new()
    {
        DataContext = new RuleEditorViewModel(new RuleDefinition { Name = "orders", FilterType = "Correlation", MessageId = "order-1", ContentType = "application/json" }, true), Height = 400
    };

    static SubscriptionEditorDialog BuildSubscriptionFixture() => new()
    {
        DataContext = new SubscriptionEditorViewModel("order-events", new SubscriptionDefinition { Name = "audit" }, true)
        { AutoDeleteOnIdle = "invalid" }, Height = 400
    };

    static TopicEditorDialog BuildTopicFixture() => new()
    {
        DataContext = new TopicEditorViewModel(new TopicDefinition { Name = "order-events" }, true)
        { AutoDeleteOnIdle = "invalid" }, Height = 400
    };

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
