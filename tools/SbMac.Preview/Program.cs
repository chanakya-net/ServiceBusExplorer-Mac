// Dev-only harness: renders SB-Mac's windows to PNGs through Avalonia's headless
// platform with real Skia drawing, so the design can be inspected without a display.
using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Azure.Messaging.ServiceBus.Administration;

using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Dialogs;
using SbMac.App.ViewModels.Tree;
using SbMac.App.Views;
using SbMac.App.Views.Dialogs;
using SbMac.Core.Connections;
using SbMac.Core.Entities;
using SbMac.Core.EventHubs;
using SbMac.Core.ImportExport;
using SbMac.Core.Messaging;

// First non-flag argument is the output directory.
var outDir = Environment.GetCommandLineArgs()
    .Skip(1)
    .FirstOrDefault(argument => !argument.StartsWith('-'))
    ?? Path.Combine(AppContext.BaseDirectory, "screenshots");

Directory.CreateDirectory(outDir);

AppBuilder.Configure<SbMac.App.App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
{
    Application.Current!.RequestedThemeVariant = variant;
    var tag = variant == ThemeVariant.Light ? "light" : "dark";

    Shoot($"main-{tag}", BuildMainWindow(), 1360, 860);
    Shoot($"activity-{tag}", BuildActivityWindow(), 1360, 860);
    Shoot($"props-{tag}", BuildMainWindow(), 1360, 860, selectTab: 1);
    Shoot($"eventhubs-{tag}", BuildEventHubsWindow(), 1360, 860);
    Shoot($"eventhubs-props-{tag}", BuildEventHubsWindow(), 1360, 860, selectTab: 1);
    Shoot($"connection-{tag}", new ConnectionDialog { DataContext = new ConnectionDialogViewModel(null) }, 620, 470);
    Shoot($"connection-eventhubs-{tag}",
        new ConnectionDialog { DataContext = BuildEventHubsConnectionViewModel() }, 620, 800);
    Shoot($"send-{tag}", new SendMessageDialog { DataContext = BuildSendViewModel() }, 760, 700);
    Shoot($"queue-{tag}", new QueueEditorDialog
    {
        DataContext = new QueueEditorViewModel(new QueueDefinition { Name = "orders", MaxDeliveryCount = 10 }, false)
    }, 620, 640);
}

Console.WriteLine("done -> " + outDir);
return;

void Shoot(string name, Window window, int width, int height, int selectTab = -1)
{
    window.Width = width;
    window.Height = height;
    window.Show();

    // Let bindings settle and the compositor produce a frame before capturing.
    for (var i = 0; i < 12; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    if (selectTab >= 0)
    {
        var tabs = window.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
        if (tabs is not null)
        {
            tabs.SelectedIndex = selectTab;
        }

        for (var i = 0; i < 12; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    var frame = window.CaptureRenderedFrame();
    var path = Path.Combine(outDir, name + ".png");

    if (frame is null)
    {
        Console.WriteLine($"!! {name}: no frame");
    }
    else
    {
        frame.Save(path);
        Console.WriteLine($"   {name} -> {path}");
    }

    window.Close();
}

MainWindow BuildMainWindow()
{
    var viewModel = new MainWindowViewModel { StatusText = "Ready" };

    var connection = new NamespaceConnection
    {
        Name = "contoso-prod",
        AuthenticationMode = AuthenticationMode.EntraId,
        FullyQualifiedNamespace = "contoso-prod.servicebus.windows.net"
    };

    var ns = new NamespaceNodeViewModel(connection) { IsExpanded = true };

    var queues = new QueueFolderNodeViewModel { Parent = ns, IsExpanded = true, Detail = "4" };
    AddQueue(queues, "orders", 1284, 17, 0);
    AddQueue(queues, "orders-retry", 42, 0, 6);
    AddQueue(queues, "payments", 0, 3, 0);
    AddQueue(queues, "legacy-import", 0, 0, 0, disabled: true);

    var topics = new TopicFolderNodeViewModel { Parent = ns, IsExpanded = true, Detail = "2" };
    var orderEvents = AddTopic(topics, "order-events", "3 subscriptions");
    AddSubscription(orderEvents, "order-events", "audit", 220, 0);
    AddSubscription(orderEvents, "order-events", "billing", 18, 4);
    AddSubscription(orderEvents, "order-events", "search-index", 0, 0);
    AddTopic(topics, "shipment-events", "1 subscription");

    ns.Children.Add(queues);
    ns.Children.Add(topics);
    viewModel.Namespaces.Add(ns);

    // Select a queue so the header, toolbar state and message list all show real content.
    var selected = (QueueNodeViewModel)queues.Children[0];
    selected.IsSelected = true;
    viewModel.SelectedNode = selected;

    foreach (var record in SampleMessages())
    {
        viewModel.Messages.Add(new MessageRowViewModel(record));
    }

    viewModel.SelectedMessage = viewModel.Messages[0];

    viewModel.Log.Insert(0, "09:41:22  Peeked 6 message(s) from orders.");
    viewModel.Log.Insert(0, "09:41:20  Connected to contoso-prod.servicebus.windows.net.");

    return new MainWindow { DataContext = viewModel };
}

// Several operations at once, which is the state the activity tray exists for.
MainWindow BuildActivityWindow()
{
    var window = BuildMainWindow();
    var viewModel = (MainWindowViewModel)window.DataContext!;

    viewModel.IsActivityExpanded = true;

    var purge = new OperationViewModel(OperationKind.Purge, "Purging orders-retry…");
    purge.Report("18,400 deleted", 18_400, 42_000);

    var peek = new OperationViewModel(OperationKind.Read, "Peeking 100 message(s) from orders…");

    var refresh = new OperationViewModel(OperationKind.Refresh, "Refreshing contoso-prod…");
    refresh.Report("order-events / billing");

    var connect = new OperationViewModel(OperationKind.Connect, "Connecting to contoso-staging…");
    connect.Finish(OperationState.Failed, "The messaging entity could not be found.");

    var send = new OperationViewModel(OperationKind.Send, "Sending 3 message(s) to orders…");
    send.Finish(OperationState.Completed);

    foreach (var operation in new[] { send, connect, purge, refresh, peek })
    {
        viewModel.Operations.Add(operation);
    }

    return window;
}

MainWindow BuildEventHubsWindow()
{
    var viewModel = new MainWindowViewModel { StatusText = "Ready" };

    var ns = new NamespaceNodeViewModel(new NamespaceConnection
    {
        Name = "contoso-telemetry",
        Kind = NamespaceKind.EventHubs,
        AuthenticationMode = AuthenticationMode.EntraId,
        FullyQualifiedNamespace = "contoso-telemetry.servicebus.windows.net"
    })
    { IsExpanded = true };

    var folder = new EventHubFolderNodeViewModel { Parent = ns, IsExpanded = true, Detail = "2" };

    var telemetry = AddEventHub(folder, "device-telemetry", ["0", "1", "2", "3"], ["$Default", "analytics"]);
    AddPartition(telemetry, "0", 1, 84_120);
    AddPartition(telemetry, "1", 1, 83_902);
    AddPartition(telemetry, "2", 1, 84_311);
    AddPartition(telemetry, "3", 1, 0, isEmpty: true);

    AddEventHub(folder, "audit-trail", ["0", "1"], ["$Default"]);

    ns.Children.Add(folder);
    viewModel.Namespaces.Add(ns);

    var selected = (EventHubNodeViewModel)folder.Children[0];
    selected.IsSelected = true;
    viewModel.SelectedNode = selected;

    foreach (var record in SampleEvents())
    {
        viewModel.Messages.Add(new MessageRowViewModel(record));
    }

    viewModel.SelectedMessage = viewModel.Messages[0];

    viewModel.Log.Insert(0, "09:41:26  Read 6 event(s) from device-telemetry.");
    viewModel.Log.Insert(0, "09:41:20  Connected to contoso-telemetry.servicebus.windows.net.");

    return new MainWindow { DataContext = viewModel };
}

EventHubNodeViewModel AddEventHub(
    TreeNodeViewModel parent,
    string name,
    IReadOnlyList<string> partitions,
    IReadOnlyList<string> consumerGroups)
{
    var node = new EventHubNodeViewModel(
        new EventHubEntity(name, DateTimeOffset.Now.AddMonths(-8), partitions, consumerGroups))
    {
        Parent = parent,
        IsExpanded = true
    };

    parent.Children.Add(node);
    return node;
}

void AddPartition(TreeNodeViewModel parent, string id, long beginning, long last, bool isEmpty = false)
{
    parent.Children.Add(new PartitionNodeViewModel(
        new PartitionEntity("device-telemetry", id, beginning, last, DateTimeOffset.Now.AddSeconds(-9), isEmpty))
    {
        Parent = parent
    });
}

IEnumerable<MessageRecord> SampleEvents()
{
    var now = DateTimeOffset.Now;

    for (var index = 0; index < 6; index++)
    {
        var sequence = 84_115 + index;
        yield return new MessageRecord
        {
            Body = BinaryData.FromString(
                $"{{ \"deviceId\": \"probe-{7 + index}\", \"tempC\": {18.4 + index:0.0}, \"battery\": {96 - index} }}"),
            SourceEntityPath = "device-telemetry",
            MessageId = $"e1c7{sequence:x}-9a20-4f31-b6d4-{sequence:x8}",
            SequenceNumber = sequence,
            EnqueuedTime = now.AddSeconds(-30 + (index * 5)),
            ContentType = "application/json",
            PartitionKey = $"probe-{7 + index}",
            Event = new EventOrigin((index % 4).ToString(), (112_233 + (index * 512)).ToString()),
            ApplicationProperties = new Dictionary<string, object?>
            {
                ["Subject"] = "device-telemetry",
                ["firmware"] = "4.2.1",
                ["site"] = "rotterdam-2"
            }
        };
    }
}

ConnectionDialogViewModel BuildEventHubsConnectionViewModel() =>
    new(new NamespaceConnection
    {
        Name = "contoso-telemetry",
        Kind = NamespaceKind.EventHubs,
        AuthenticationMode = AuthenticationMode.EntraId,
        FullyQualifiedNamespace = "contoso-telemetry.servicebus.windows.net",
        EntraCredentialKind = EntraCredentialKind.AzureCli,
        EventHubNames = ["device-telemetry", "audit-trail"],
        ConsumerGroup = "analytics"
    });

void AddQueue(TreeNodeViewModel parent, string name, long active, long dead, long scheduled, bool disabled = false)
{
    var properties = New<QueueProperties>(name);
    if (disabled)
    {
        properties.Status = EntityStatus.Disabled;
    }

    var runtime = New<QueueRuntimeProperties>(name);
    Set(runtime, "ActiveMessageCount", active);
    Set(runtime, "DeadLetterMessageCount", dead);
    Set(runtime, "ScheduledMessageCount", scheduled);
    Set(runtime, "SizeInBytes", active * 812);

    parent.Children.Add(new QueueNodeViewModel(new QueueEntity(properties, runtime)) { Parent = parent });
}

TopicNodeViewModel AddTopic(TreeNodeViewModel parent, string name, string detail)
{
    var node = new TopicNodeViewModel(new TopicEntity(New<TopicProperties>(name), null))
    {
        Parent = parent,
        IsExpanded = true,
        Detail = detail
    };

    parent.Children.Add(node);
    return node;
}

void AddSubscription(TreeNodeViewModel parent, string topic, string name, long active, long dead)
{
    var properties = (SubscriptionProperties)Activator.CreateInstance(
        typeof(SubscriptionProperties),
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        [topic, name],
        null)!;

    var runtime = (SubscriptionRuntimeProperties)Activator.CreateInstance(
        typeof(SubscriptionRuntimeProperties),
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        [topic, name],
        null)!;

    Set(runtime, "ActiveMessageCount", active);
    Set(runtime, "DeadLetterMessageCount", dead);

    parent.Children.Add(new SubscriptionNodeViewModel(new SubscriptionEntity(properties, runtime)) { Parent = parent });
}

SendMessageViewModel BuildSendViewModel()
{
    var viewModel = new SendMessageViewModel(new SendMessageRequest("orders"))
    {
        Body = "{\n  \"orderId\": 40921,\n  \"region\": \"emea\",\n  \"total\": 128.40\n}",
        ContentType = "application/json",
        Subject = "order-created"
    };

    viewModel.Properties.Add(new PropertyRowViewModel { Key = "region", Value = "emea" });
    viewModel.Properties.Add(new PropertyRowViewModel { Key = "priority", Value = "high" });
    return viewModel;
}

IEnumerable<MessageRecord> SampleMessages()
{
    var now = DateTimeOffset.Now;

    yield return Record(40921, "{\n  \"orderId\": 40921,\n  \"region\": \"emea\",\n  \"total\": 128.40\n}",
        now.AddMinutes(-3), "order-created", 1, null);
    yield return Record(40922, "{ \"orderId\": 40922, \"region\": \"apac\", \"total\": 76.10 }",
        now.AddMinutes(-3), "order-created", 1, null);
    yield return Record(40923, "{ \"orderId\": 40923, \"region\": \"amer\", \"total\": 1204.00 }",
        now.AddMinutes(-2), "order-created", 2, null);
    yield return Record(40924, "<order id=\"40924\"><region>emea</region></order>",
        now.AddMinutes(-2), "order-legacy", 1, null);
    yield return Record(40925, "{ \"orderId\": 40925, \"region\": \"emea\", \"total\": 9.99 }",
        now.AddMinutes(-1), "order-created", 9, "MaxDeliveryCountExceeded");
    yield return Record(40926, "plain text payload from an older producer",
        now, "order-created", 1, null);

    MessageRecord Record(long seq, string body, DateTimeOffset at, string subject, int tries, string? deadLetter) => new()
    {
        Body = BinaryData.FromString(body),
        SourceEntityPath = "orders",
        MessageId = $"a3f9{seq:x}-4c1e-4b7a-9f2d-{seq:x8}",
        SequenceNumber = seq,
        EnqueuedTime = at,
        ExpiresAt = at.AddDays(14),
        TimeToLive = TimeSpan.FromDays(14),
        DeliveryCount = tries,
        Subject = subject,
        ContentType = body.StartsWith('{') ? "application/json" : null,
        CorrelationId = $"corr-{seq}",
        DeadLetterReason = deadLetter,
        DeadLetterErrorDescription = deadLetter is null ? null : "The message could not be processed after 9 attempts.",
        IsDeadLetter = deadLetter is not null,
        ApplicationProperties = new Dictionary<string, object?>
        {
            ["region"] = "emea",
            ["producer"] = "checkout-api",
            ["schemaVersion"] = 3
        }
    };
}

// The SDK keeps these constructors internal because they normally come from the service.
// A render harness is the one place it's reasonable to reach past that.
static T New<T>(string name) => (T)Activator.CreateInstance(
    typeof(T), BindingFlags.Instance | BindingFlags.NonPublic, null, [name], null)!;

static void Set(object target, string property, object value) =>
    target.GetType().GetProperty(property)!.SetValue(target, value);
