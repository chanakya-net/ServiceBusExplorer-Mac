using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Dialogs;
using SbMac.App.ViewModels.Tree;
using SbMac.App.Views;
using SbMac.App.Views.Dialogs;

using SbMac.Core.Connections;
using SbMac.Core.EventHubs;
using SbMac.Core.ImportExport;

using Xunit;

namespace SbMac.Tests;

/// <summary>
/// Loads every window for real. A typo in a binding path, a missing style resource or a
/// bad template only shows up when the XAML is actually instantiated, so these tests
/// build each window rather than asserting on the view models alone.
/// </summary>
public class UiSmokeTests
{
    [AvaloniaFact]
    public void MainWindowLoadsWithItsViewModel()
    {
        var window = new MainWindow { DataContext = new MainWindowViewModel() };
        window.Show();

        Assert.Equal("Service Bus Explorer for Mac", window.Title);
        Assert.NotNull(window.Content);
    }

    [AvaloniaFact]
    public void MainWindowRegistersItselfAsTheDialogHost()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        // Without this the commands silently no-op, because every one of them returns
        // early when Ui is null.
        Assert.NotNull(viewModel.Ui);
    }

    [AvaloniaFact]
    public void ConnectionDialogLoadsForANewNamespace()
    {
        var window = new ConnectionDialog { DataContext = new ConnectionDialogViewModel(null) };
        window.Show();

        Assert.Equal("Add namespace", window.Title);
    }

    [AvaloniaFact]
    public void ConnectionDialogLoadsForAnExistingNamespace()
    {
        var connection = new NamespaceConnection
        {
            Name = "contoso",
            AuthenticationMode = AuthenticationMode.EntraId,
            FullyQualifiedNamespace = "contoso.servicebus.windows.net",
            EntraCredentialKind = EntraCredentialKind.AzureCli
        };

        var window = new ConnectionDialog { DataContext = new ConnectionDialogViewModel(connection) };
        window.Show();

        Assert.Equal("Edit namespace", window.Title);
    }

    [AvaloniaFact]
    public void ConnectionDialogLoadsForAnEventHubsNamespace()
    {
        var connection = new NamespaceConnection
        {
            Name = "contoso",
            Kind = NamespaceKind.EventHubs,
            EventHubNames = ["telemetry"],
            ConsumerGroup = "analytics"
        };

        var window = new ConnectionDialog { DataContext = new ConnectionDialogViewModel(connection) };
        window.Show();

        Assert.Equal("Edit namespace", window.Title);
    }

    [AvaloniaFact]
    public void SendMessageDialogLoads()
    {
        var window = new SendMessageDialog
        {
            DataContext = new SendMessageViewModel(new SendMessageRequest("orders"))
        };

        window.Show();

        Assert.Equal("Send to orders", window.Title);
    }

    [AvaloniaFact]
    public void SendMessageDialogLoadsForAnEventHub()
    {
        var window = new SendMessageDialog
        {
            DataContext = new SendMessageViewModel(new SendMessageRequest("telemetry", IsEventHub: true))
        };

        window.Show();

        Assert.Equal("Send to telemetry", window.Title);
    }

    /// <summary>
    /// The toolbar's enabled states are bound to properties that only an Event Hubs
    /// selection exercises, so the window is shown with one selected.
    /// </summary>
    [AvaloniaFact]
    public void MainWindowRendersWithAnEventHubSelected()
    {
        var namespaceNode = new NamespaceNodeViewModel(new NamespaceConnection
        {
            Name = "contoso",
            Kind = NamespaceKind.EventHubs
        });

        var folder = new EventHubFolderNodeViewModel { Parent = namespaceNode };
        var hub = new EventHubNodeViewModel(
            new EventHubEntity("telemetry", DateTimeOffset.UtcNow, ["0", "1"], ["$Default"])) { Parent = folder };

        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        viewModel.Namespaces.Add(namespaceNode);
        viewModel.SelectedNode = hub;

        Assert.Equal("Events", viewModel.MessageSourceLabel);
        Assert.NotNull(window.GetControl<DataGrid>("MessageGrid"));
    }

    /// <summary>
    /// The activity rows resolve their bar colour through a converter, which only runs when
    /// the template is actually instantiated — so the panel is shown with entries in it.
    /// </summary>
    [AvaloniaFact]
    public void MainWindowRendersTheActivityPanel()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        var purging = new OperationViewModel(OperationKind.Purge, "Purging orders…");
        purging.Report("4,000 deleted", 4_000, 20_000);

        var failed = new OperationViewModel(OperationKind.Connect, "Connecting to contoso…");
        failed.Finish(OperationState.Failed, "No such host is known.");

        viewModel.Operations.Add(purging);
        viewModel.Operations.Add(failed);
        viewModel.IsActivityExpanded = true;

        Assert.True(viewModel.IsBusy);
        Assert.Equal("Purging orders…", viewModel.ActivitySummary);
    }

    [AvaloniaFact]
    public void QueueEditorLoadsInBothCreateAndEditModes()
    {
        var create = new QueueEditorDialog
        {
            DataContext = new QueueEditorViewModel(new QueueDefinition { Name = string.Empty }, isNew: true)
        };
        create.Show();
        Assert.Equal("Create queue", create.Title);

        var edit = new QueueEditorDialog
        {
            DataContext = new QueueEditorViewModel(new QueueDefinition { Name = "orders" }, isNew: false)
        };
        edit.Show();
        Assert.Contains("orders", edit.Title);
    }

    [AvaloniaFact]
    public void TopicEditorLoads()
    {
        var window = new TopicEditorDialog
        {
            DataContext = new TopicEditorViewModel(new TopicDefinition { Name = "order-events" }, isNew: false)
        };

        window.Show();
        Assert.Contains("order-events", window.Title);
    }

    [AvaloniaFact]
    public void SubscriptionEditorLoads()
    {
        var window = new SubscriptionEditorDialog
        {
            DataContext = new SubscriptionEditorViewModel(
                "order-events",
                new SubscriptionDefinition { Name = "audit" },
                isNew: false)
        };

        window.Show();
        Assert.Contains("audit", window.Title);
    }

    [AvaloniaFact]
    public void RuleEditorLoads()
    {
        var window = new RuleEditorDialog
        {
            DataContext = new RuleEditorViewModel(new RuleDefinition { Name = "emea-only" }, isNew: false)
        };

        window.Show();
        Assert.Contains("emea-only", window.Title);
    }

    [AvaloniaFact]
    public void MessageGridIsBoundToTheViewModelsMessages()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        var grid = window.GetControl<DataGrid>("MessageGrid");

        Assert.Same(viewModel.Messages, grid.ItemsSource);
    }

    [AvaloniaFact]
    public void NamespaceTreeIsBoundToTheViewModelsNamespaces()
    {
        var viewModel = new MainWindowViewModel();
        var window = new MainWindow { DataContext = viewModel };
        window.Show();

        var tree = window.GetControl<TreeView>("EntityTree");

        Assert.Same(viewModel.Namespaces, tree.ItemsSource);
    }
}
