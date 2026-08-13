using System.Collections.ObjectModel;
using System.Text;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SbMac.App.Services;
using SbMac.App.ViewModels.Dialogs;
using SbMac.App.ViewModels.Tree;

using SbMac.Core;
using SbMac.Core.Connections;
using SbMac.Core.Entities;
using SbMac.Core.EventHubs;
using SbMac.Core.ImportExport;
using SbMac.Core.Messaging;

namespace SbMac.App.ViewModels;

/// <summary>
/// Drives the whole window: the namespace tree on the left, the message list and
/// detail pane on the right, and every command in the toolbar and context menus.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    readonly ConnectionStore connectionStore = ConnectionStore.CreateDefault();

    /// <summary>
    /// Descriptions of the operations currently in flight. Different work runs side by
    /// side; this only stops the same request being started twice, which is what a
    /// double-tapped keyboard shortcut looks like.
    /// </summary>
    readonly HashSet<string> activeOperations = [];

    /// <summary>How many finished operations stay in the tray before the oldest is dropped.</summary>
    const int FinishedOperationsKept = 5;

    public MainWindowViewModel()
    {
        Namespaces = [];
        Messages = [];
        Log = [];
        Operations = [];

        // The placeholders are derived from collection contents, which don't raise
        // property changes of their own.
        Namespaces.CollectionChanged += (_, eventArgs) =>
        {
            OnPropertyChanged(nameof(HasNoNamespaces));

            if (eventArgs.NewItems is not null)
            {
                foreach (NamespaceNodeViewModel namespaceNode in eventArgs.NewItems)
                {
                    namespaceNode.ApplySearch(EntitySearchText);
                }
            }
        };
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoMessages));
        Operations.CollectionChanged += (_, _) => RaiseActivityChanged();
    }

    /// <summary>Set by the window once it's constructed; every dialog goes through this.</summary>
    public IUiServices? Ui { get; set; }

    public ObservableCollection<NamespaceNodeViewModel> Namespaces { get; }

    [ObservableProperty]
    string entitySearchText = string.Empty;

    partial void OnEntitySearchTextChanged(string value)
    {
        foreach (var namespaceNode in Namespaces)
        {
            namespaceNode.ApplySearch(value);
        }
    }

    public ObservableCollection<MessageRowViewModel> Messages { get; }

    public ObservableCollection<string> Log { get; }

    /// <summary>
    /// Everything the window is doing, and the last few things it did. Each entry owns its
    /// own progress and cancellation, which is what lets a peek run while a namespace is
    /// still loading its topics.
    /// </summary>
    public ObservableCollection<OperationViewModel> Operations { get; }

    // ------------------------------------------------------------- selection

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedQueue))]
    [NotifyPropertyChangedFor(nameof(SelectedTopic))]
    [NotifyPropertyChangedFor(nameof(SelectedSubscription))]
    [NotifyPropertyChangedFor(nameof(SelectedEventHub))]
    [NotifyPropertyChangedFor(nameof(SelectedPartition))]
    [NotifyPropertyChangedFor(nameof(SelectedNamespace))]
    [NotifyPropertyChangedFor(nameof(HasMessageEntity))]
    [NotifyPropertyChangedFor(nameof(EmptyMessagesHint))]
    [NotifyPropertyChangedFor(nameof(MessageSourceLabel))]
    [NotifyPropertyChangedFor(nameof(CanCreateSubscription))]
    [NotifyPropertyChangedFor(nameof(CanEditEntity))]
    [NotifyPropertyChangedFor(nameof(CanDeleteEntity))]
    [NotifyPropertyChangedFor(nameof(CanManageEntities))]
    [NotifyPropertyChangedFor(nameof(CanUseBrokerActions))]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(CanEditAndResend))]
    [NotifyPropertyChangedFor(nameof(IsEventHubSelection))]
    [NotifyPropertyChangedFor(nameof(EntityHeader))]
    [NotifyPropertyChangedFor(nameof(EntitySummary))]
    TreeNodeViewModel? selectedNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedMessage))]
    [NotifyPropertyChangedFor(nameof(CanEditAndResend))]
    [NotifyPropertyChangedFor(nameof(BodyText))]
    [NotifyPropertyChangedFor(nameof(PropertiesText))]
    [NotifyPropertyChangedFor(nameof(PropertyEntries))]
    [NotifyPropertyChangedFor(nameof(BodyFormatLabel))]
    MessageRowViewModel? selectedMessage;

    /// <summary>Kept in sync by the grid, so multi-message actions know their targets.</summary>
    public ObservableCollection<MessageRowViewModel> SelectedMessages { get; } = [];

    public NamespaceNodeViewModel? SelectedNamespace => SelectedNode?.Namespace;

    public QueueNodeViewModel? SelectedQueue => SelectedNode as QueueNodeViewModel;

    public TopicNodeViewModel? SelectedTopic => SelectedNode as TopicNodeViewModel;

    public SubscriptionNodeViewModel? SelectedSubscription => SelectedNode as SubscriptionNodeViewModel;

    public EventHubNodeViewModel? SelectedEventHub => SelectedNode as EventHubNodeViewModel;

    public PartitionNodeViewModel? SelectedPartition => SelectedNode as PartitionNodeViewModel;

    /// <summary>True when the selection is something messages or events can be read from.</summary>
    public bool HasMessageEntity =>
        SelectedQueue is not null ||
        SelectedSubscription is not null ||
        CurrentEventHubName is not null;

    public bool HasSelectedMessage => SelectedMessage is not null;

    /// <summary>Needs both a message to start from and somewhere to send the result.</summary>
    public bool CanEditAndResend => HasSelectedMessage && CanSend;

    public bool CanCreateSubscription => SelectedTopic is not null;

    public bool CanEditEntity => SelectedQueue is not null || SelectedTopic is not null || SelectedSubscription is not null;

    public bool CanDeleteEntity => CanEditEntity;

    /// <summary>
    /// True when the selected namespace has entities SB-Mac can create, edit or move.
    /// Event hubs are provisioned through ARM rather than the data plane, so they are
    /// browsed here but not managed here.
    /// </summary>
    public bool CanManageEntities => SelectedNamespace is { IsEventHubs: false };

    /// <summary>
    /// True when the selection supports the actions that depend on a Service Bus broker:
    /// receiving, deleting, purging and dead-lettering. Event Hubs has none of them — it
    /// is an append-only log with time-based retention, and reads never remove anything.
    /// </summary>
    public bool CanUseBrokerActions => SelectedQueue is not null || SelectedSubscription is not null;

    /// <summary>True when the current selection lives in an event hub rather than Service Bus.</summary>
    public bool IsEventHubSelection => CurrentEventHubName is not null;

    /// <summary>True when there is somewhere to send to and a live client to send with.</summary>
    public bool CanSend =>
        CurrentSendTarget is not null &&
        (IsEventHubSelection
            ? SelectedNamespace?.EventHubs is not null
            : SelectedNamespace?.Messages is not null);

    /// <summary>The message path the read/write commands act on. Null for Event Hubs.</summary>
    EntityPath? CurrentPath => SelectedQueue?.Path ?? SelectedSubscription?.Path;

    /// <summary>The event hub the read/write commands act on, whether a hub or a partition is selected.</summary>
    string? CurrentEventHubName => SelectedEventHub?.Entity.Name ?? SelectedPartition?.EventHubName;

    /// <summary>The partition to read from, or null to read across all of them.</summary>
    string? CurrentPartitionId => SelectedPartition?.PartitionId;

    /// <summary>Where a resubmit or send goes: the queue itself, the subscription's topic, or the event hub.</summary>
    string? CurrentSendTarget =>
        SelectedQueue?.Entity.Name ??
        SelectedSubscription?.Entity.TopicName ??
        SelectedTopic?.Entity.Name ??
        CurrentEventHubName;

    // ------------------------------------------------------------ view state

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MessageSourceLabel))]
    [NotifyPropertyChangedFor(nameof(EmptyMessagesHint))]
    bool showDeadLetter;

    [ObservableProperty]
    int fetchCount = 100;

    [ObservableProperty]
    string statusText = "Ready";

    /// <summary>True while the activity tray is showing every operation rather than its bars alone.</summary>
    [ObservableProperty]
    bool isActivityExpanded;

    [ObservableProperty]
    string messageFilter = string.Empty;

    /// <summary>True while any operation is running. Nothing is gated on it — it's for display.</summary>
    public bool IsBusy => Operations.Any(operation => operation.IsRunning);

    public bool HasOperations => Operations.Count > 0;

    public bool HasFinishedOperations => Operations.Any(operation => operation.IsFinished);

    public int RunningOperationCount => Operations.Count(operation => operation.IsRunning);

    /// <summary>
    /// What the collapsed tray says next to the bars. One operation names itself; several
    /// are counted, because their names won't fit and the bars already say which is which.
    /// </summary>
    public string ActivitySummary => RunningOperationCount switch
    {
        0 => HasOperations ? "Recent activity" : string.Empty,
        1 => Operations.First(operation => operation.IsRunning).Title,
        var count => $"{count} operations running"
    };

    public string MessageSourceLabel => CurrentEventHubName is not null
        ? "Events"
        : ShowDeadLetter ? "Dead-letter queue" : "Active messages";

    public string BodyText => SelectedMessage?.BodyText ?? string.Empty;

    public string PropertiesText => SelectedMessage?.PropertiesText ?? string.Empty;

    public IReadOnlyList<MessagePropertyEntry> PropertyEntries =>
        SelectedMessage?.PropertyEntries ?? [];

    public string BodyFormatLabel => SelectedMessage?.BodyFormatLabel ?? string.Empty;

    /// <summary>Drives the first-run placeholder in the sidebar.</summary>
    public bool HasNoNamespaces => Namespaces.Count == 0;

    /// <summary>Drives the placeholder shown in place of an empty message grid.</summary>
    public bool HasNoMessages => Messages.Count == 0;

    /// <summary>Short hint shown where the message list would be, when it's empty.</summary>
    public string EmptyMessagesHint => (HasMessageEntity, CurrentEventHubName is not null) switch
    {
        (true, true) =>
            "No events loaded. Choose Peek to read the most recent events — Event Hubs reads never remove anything.",
        (true, false) =>
            $"No messages loaded. Choose Peek to read the {(ShowDeadLetter ? "dead-letter queue" : "first messages")} without removing them.",
        _ => "Select a queue, subscription or event hub in the sidebar to browse its messages."
    };

    public string EntityHeader => SelectedNode switch
    {
        QueueNodeViewModel queue => queue.Entity.Name,
        SubscriptionNodeViewModel subscription => $"{subscription.Entity.TopicName} / {subscription.Entity.Name}",
        TopicNodeViewModel topic => topic.Entity.Name,
        EventHubNodeViewModel hub => hub.Entity.Name,
        PartitionNodeViewModel partition => $"{partition.EventHubName} / partition {partition.PartitionId}",
        NamespaceNodeViewModel ns => ns.Title,
        _ => "No entity selected"
    };

    public string EntitySummary => SelectedNode switch
    {
        QueueNodeViewModel queue =>
            $"{queue.Entity.ActiveMessageCount:N0} active · {queue.Entity.DeadLetterMessageCount:N0} dead-lettered · " +
            $"{queue.Entity.ScheduledMessageCount:N0} scheduled · {MessageRowViewModel.FormatBytes(queue.Entity.SizeInBytes)}",

        SubscriptionNodeViewModel subscription =>
            $"{subscription.Entity.ActiveMessageCount:N0} active · {subscription.Entity.DeadLetterMessageCount:N0} dead-lettered",

        TopicNodeViewModel topic =>
            $"{topic.Entity.SubscriptionCount} subscriptions · {MessageRowViewModel.FormatBytes(topic.Entity.SizeInBytes)}",

        EventHubNodeViewModel hub => DescribeEventHub(hub),

        PartitionNodeViewModel partition => partition.Entity.IsEmpty
            ? "No events retained in this partition."
            : $"{partition.Entity.RetainedEventCount:N0} events retained · sequence {partition.Entity.BeginningSequenceNumber:N0}–" +
              $"{partition.Entity.LastEnqueuedSequenceNumber:N0} · last at {partition.Entity.LastEnqueuedTime.ToLocalTime():g}",

        NamespaceNodeViewModel { IsConnected: true } ns => ns.Connection.ResolvedNamespace,
        NamespaceNodeViewModel ns => ns.ConnectionError ?? "Not connected",

        _ => "Select a queue, subscription or event hub to browse its messages."
    };

    string DescribeEventHub(EventHubNodeViewModel hub)
    {
        if (hub.Entity.Error is { } error)
        {
            return error;
        }

        var parts = new List<string>(4)
        {
            $"{hub.Entity.PartitionCount} partitions",
            $"{hub.RetainedEventCount:N0} events retained"
        };

        if (hub.Entity.ConsumerGroups.Count > 0)
        {
            parts.Add($"consumer groups: {string.Join(", ", hub.Entity.ConsumerGroups)}");
        }

        // Which group the reads go through changes what a competing application sees in
        // its own checkpoints, so it belongs on screen rather than buried in settings.
        if (SelectedNamespace is { } ns)
        {
            parts.Add($"reading through {ns.Connection.EffectiveConsumerGroup}");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Event Hubs has no dead-letter sub-queue, so the toggle is cleared rather than left
    /// on from a previous Service Bus selection — otherwise Peek would read the wrong
    /// thing the moment the user came back.
    /// </summary>
    partial void OnSelectedNodeChanged(TreeNodeViewModel? value)
    {
        if (CurrentEventHubName is not null)
        {
            ShowDeadLetter = false;
        }
    }

    // ------------------------------------------------------------- lifecycle

    /// <summary>Loads saved namespaces. Called once when the window opens.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var missingSecrets = 0;

            foreach (var connection in await connectionStore.LoadAsync().ConfigureAwait(true))
            {
                var node = new NamespaceNodeViewModel(connection);

                // A saved namespace whose secret is gone from the keychain looks identical
                // to a healthy one until you try to connect. Flag it up front instead.
                if (connection.AuthenticationMode == AuthenticationMode.ConnectionString &&
                    string.IsNullOrWhiteSpace(connection.ConnectionString))
                {
                    node.ConnectionError = "Connection string missing from the keychain — edit this namespace to re-enter it.";
                    missingSecrets++;
                }

                Namespaces.Add(node);
            }

            AppendLog(Namespaces.Count == 0
                ? "No saved namespaces yet — use Add namespace to connect."
                : $"Loaded {Namespaces.Count} saved namespace(s).");

            if (missingSecrets > 0)
            {
                AppendLog(
                    $"{missingSecrets} namespace(s) have no connection string in the keychain. " +
                    "Select one and choose Edit Namespace to re-enter it.");
            }
        }
        catch (Exception exception)
        {
            AppendLog($"Could not load saved namespaces: {exception.Message}");
        }
    }

    /// <summary>Disposes every open session. Called when the app is shutting down.</summary>
    public void Shutdown()
    {
        CancelAllOperations();

        foreach (var node in Namespaces)
        {
            // Shutdown can't await, and the AMQP links are torn down by the process
            // exiting anyway; this just gives them the chance to close politely.
            _ = node.DisposeAsync().AsTask();
        }
    }

    // ---------------------------------------------------- connection commands

    [RelayCommand]
    async Task AddConnectionAsync()
    {
        if (Ui is null)
        {
            return;
        }

        var connection = await Ui.EditConnectionAsync(null).ConfigureAwait(true);
        if (connection is null)
        {
            return;
        }

        var node = new NamespaceNodeViewModel(connection);
        Namespaces.Add(node);
        await PersistConnectionsAsync().ConfigureAwait(true);

        SelectedNode = node;
        await ConnectNamespaceAsync(node).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task EditConnectionAsync(NamespaceNodeViewModel? node)
    {
        node ??= SelectedNamespace;
        if (node is null || Ui is null)
        {
            return;
        }

        var updated = await Ui.EditConnectionAsync(node.Connection).ConfigureAwait(true);
        if (updated is null)
        {
            return;
        }

        node.UpdateConnection(updated);
        await PersistConnectionsAsync().ConfigureAwait(true);

        // Settings changed, so the old session is stale — reconnect if it was open.
        if (node.IsConnected)
        {
            await ConnectNamespaceAsync(node).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    async Task RemoveConnectionAsync(NamespaceNodeViewModel? node)
    {
        node ??= SelectedNamespace;
        if (node is null || Ui is null)
        {
            return;
        }

        var confirmed = await Ui.ConfirmAsync(
            "Remove namespace",
            $"Remove “{node.Title}” from SB-Mac?\n\nThis only forgets the saved connection. Nothing in Azure is deleted.",
            "Remove",
            destructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await node.DisposeAsync().ConfigureAwait(true);
        Namespaces.Remove(node);

        if (SelectedNode?.Namespace == node)
        {
            SelectedNode = null;
            Messages.Clear();
        }

        await connectionStore.DeleteAsync(node.Connection).ConfigureAwait(true);
        AppendLog($"Removed namespace “{node.Title}”.");
    }

    [RelayCommand]
    async Task ConnectAsync(NamespaceNodeViewModel? node)
    {
        node ??= SelectedNamespace;
        if (node is not null)
        {
            await ConnectNamespaceAsync(node).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    async Task DisconnectAsync(NamespaceNodeViewModel? node)
    {
        node ??= SelectedNamespace;
        if (node is null)
        {
            return;
        }

        await node.DisconnectAsync().ConfigureAwait(true);
        Messages.Clear();
        AppendLog($"Disconnected from “{node.Title}”.");
    }

    async Task ConnectNamespaceAsync(NamespaceNodeViewModel node)
    {
        await RunAsync(OperationKind.Connect, $"Connecting to {node.Title}…", async cancellationToken =>
        {
            await node.ConnectAsync(cancellationToken).ConfigureAwait(true);
            AppendLog($"Connected to {node.Connection.ResolvedNamespace}.");
        }).ConfigureAwait(true);
    }

    async Task PersistConnectionsAsync()
    {
        try
        {
            await connectionStore.SaveAsync(Namespaces.Select(node => node.Connection)).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // A failed save means the connection string is gone the moment the app closes.
            // Logging it quietly is how that went unnoticed before, so this is surfaced.
            AppendLog($"Could not save connections: {exception.Message}");

            if (Ui is not null)
            {
                await Ui.ShowErrorAsync(
                    "Could not save the connection",
                    $"{exception.Message}\n\n" +
                    "The namespace is usable now, but will not be remembered when SB-Mac closes.")
                    .ConfigureAwait(true);
            }
        }
    }

    // ------------------------------------------------------- refresh commands

    [RelayCommand]
    async Task RefreshAsync()
    {
        var node = SelectedNode ?? SelectedNamespace;
        if (node is null)
        {
            return;
        }

        await RunAsync(OperationKind.Refresh, $"Refreshing {node.Title}…", async cancellationToken =>
        {
            await node.RefreshAsync(cancellationToken).ConfigureAwait(true);
            RaiseEntityPropertiesChanged();
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task RefreshNamespaceAsync(NamespaceNodeViewModel? node)
    {
        node ??= SelectedNamespace;
        if (node is null)
        {
            return;
        }

        await RunAsync(OperationKind.Refresh, $"Refreshing {node.Title}…", async cancellationToken =>
        {
            await node.RefreshAsync(cancellationToken).ConfigureAwait(true);
            RaiseEntityPropertiesChanged();
        }).ConfigureAwait(true);
    }

    // ------------------------------------------------------- message commands

    [RelayCommand]
    async Task PeekAsync()
    {
        if (CurrentEventHubName is not null)
        {
            await PeekEventsAsync().ConfigureAwait(true);
            return;
        }

        var path = CurrentPath;
        var messages = SelectedNamespace?.Messages;
        if (path is null || messages is null)
        {
            return;
        }

        var deadLetter = ShowDeadLetter;
        var count = Math.Max(1, FetchCount);
        var origin = SelectedNode;

        await RunAsync(OperationKind.Read, $"Peeking {count} message(s) from {path}…", async cancellationToken =>
        {
            var records = await messages
                .PeekAsync(path, deadLetter, count, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            var label = $"{path}{(deadLetter ? "/$DeadLetterQueue" : string.Empty)}";
            AppendLog($"Peeked {records.Count} message(s) from {label}.");

            ShowMessagesFor(origin, records, label);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the most recent events from the selected hub or partition. Non-destructive by
    /// construction — Event Hubs has no operation that removes an event.
    /// </summary>
    async Task PeekEventsAsync()
    {
        var hubName = CurrentEventHubName;
        var eventHubs = SelectedNamespace?.EventHubs;
        if (hubName is null || eventHubs is null)
        {
            return;
        }

        var partitionId = CurrentPartitionId;
        var count = Math.Max(1, FetchCount);
        var source = partitionId is null ? hubName : $"{hubName}/partition {partitionId}";
        var origin = SelectedNode;

        await RunAsync(OperationKind.Read, $"Reading {count} event(s) from {source}…", async cancellationToken =>
        {
            var records = await eventHubs
                .PeekAsync(hubName, partitionId, count, cancellationToken)
                .ConfigureAwait(true);

            AppendLog($"Read {records.Count} event(s) from {source}.");

            ShowMessagesFor(origin, records, source);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task ReceiveAsync()
    {
        var path = CurrentPath;
        var messages = SelectedNamespace?.Messages;
        if (path is null || messages is null || Ui is null)
        {
            return;
        }

        var deadLetter = ShowDeadLetter;
        var count = Math.Max(1, FetchCount);

        var confirmed = await Ui.ConfirmAsync(
            "Receive and delete",
            $"Receive up to {count:N0} message(s) from {path}{(deadLetter ? "/$DeadLetterQueue" : string.Empty)}?\n\n" +
            "This removes them from the entity permanently.",
            "Receive",
            destructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        var origin = SelectedNode;

        await RunAsync(OperationKind.Receive, $"Receiving {count} message(s) from {path}…", async cancellationToken =>
        {
            var records = await messages
                .ReceiveAndDeleteAsync(path, deadLetter, count, cancellationToken)
                .ConfigureAwait(true);

            AppendLog($"Received and deleted {records.Count} message(s) from {path}.");

            ShowMessagesFor(origin, records, path.ToString());
            await RefreshNodeAsync(origin, cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task SendAsync()
    {
        var target = CurrentSendTarget;
        if (target is null || Ui is null || !CanSend)
        {
            return;
        }

        var composed = await Ui
            .ComposeMessageAsync(new SendMessageRequest(target, IsEventHub: IsEventHubSelection))
            .ConfigureAwait(true);

        if (composed is null)
        {
            return;
        }

        await SendComposedAsync(composed).ConfigureAwait(true);
    }

    /// <summary>Opens the compose dialog pre-filled from the selected message.</summary>
    [RelayCommand]
    async Task ResubmitAsync()
    {
        var target = CurrentSendTarget;
        var selected = SelectedMessage;

        if (target is null || selected is null || Ui is null || !CanSend)
        {
            return;
        }

        // A dead-lettered message records the entity it was originally sent to. That's a
        // better default than the entity we happen to be browsing, which for a forwarded
        // message can be somewhere else entirely.
        var seedTarget = selected.Record.DeadLetterSource is { Length: > 0 } source ? source : target;

        var description = selected.Record.IsDeadLetter
            ? $"Starting from dead-lettered message {selected.SequenceNumber} ({selected.Record.DeadLetterReason})."
            : $"Starting from sequence number {selected.SequenceNumber}.";

        var composed = await Ui
            .ComposeMessageAsync(new SendMessageRequest(
                seedTarget, selected.Record, description, IsEventHub: IsEventHubSelection))
            .ConfigureAwait(true);

        if (composed is null)
        {
            return;
        }

        await SendComposedAsync(composed).ConfigureAwait(true);
    }

    async Task SendComposedAsync(SendMessageResult composed)
    {
        if (IsEventHubSelection)
        {
            await PublishComposedAsync(composed).ConfigureAwait(true);
            return;
        }

        var messages = SelectedNamespace?.Messages;
        if (messages is null)
        {
            return;
        }

        var origin = SelectedNode;

        await RunAsync(
            OperationKind.Send,
            $"Sending {composed.Messages.Count} message(s) to {composed.TargetName}…",
            async cancellationToken =>
        {
            if (composed.ScheduledEnqueueTime is { } enqueueTime)
            {
                foreach (var message in composed.Messages)
                {
                    await messages
                        .ScheduleAsync(composed.TargetName, message, enqueueTime, cancellationToken)
                        .ConfigureAwait(true);
                }

                AppendLog($"Scheduled {composed.Messages.Count} message(s) to {composed.TargetName} for {enqueueTime:g}.");
            }
            else
            {
                var sent = await messages
                    .SendAsync(composed.TargetName, composed.Messages, cancellationToken)
                    .ConfigureAwait(true);

                AppendLog($"Sent {sent} message(s) to {composed.TargetName}.");
            }

            await RefreshNodeAsync(origin, cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Publishes composed events to the selected hub.
    /// </summary>
    /// <remarks>
    /// Routing follows what the user asked for: an explicit partition key wins, because it
    /// is the only one of the two they can have typed deliberately. Failing that, having a
    /// partition selected publishes straight to it, which is what makes "select a partition,
    /// send, peek" work as one loop.
    /// </remarks>
    async Task PublishComposedAsync(SendMessageResult composed)
    {
        var eventHubs = SelectedNamespace?.EventHubs;
        if (eventHubs is null)
        {
            return;
        }

        var partitionKey = composed.Messages.Count > 0 ? composed.Messages[0].PartitionKey : null;
        var partitionId = string.IsNullOrWhiteSpace(partitionKey) ? CurrentPartitionId : null;

        var events = composed.Messages.Select(EventMapper.ToEventData).ToList();
        var origin = SelectedNode;

        await RunAsync(
            OperationKind.Send,
            $"Publishing {events.Count} event(s) to {composed.TargetName}…",
            async cancellationToken =>
        {
            var sent = await eventHubs
                .SendAsync(composed.TargetName, events, partitionKey, partitionId, cancellationToken)
                .ConfigureAwait(true);

            var routing = partitionKey is { Length: > 0 } key
                ? $" with partition key “{key}”"
                : partitionId is not null ? $" to partition {partitionId}" : string.Empty;

            AppendLog($"Published {sent} event(s) to {composed.TargetName}{routing}.");

            await RefreshNodeAsync(origin, cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// Sends the selected dead-lettered messages back to their entity unchanged, and
    /// optionally removes the dead-letter copies.
    /// </summary>
    [RelayCommand]
    async Task ResubmitSelectedAsync()
    {
        var target = CurrentSendTarget;
        var path = CurrentPath;
        var messages = SelectedNamespace?.Messages;
        var selected = SelectedMessages.ToList();

        if (target is null || path is null || messages is null || Ui is null || selected.Count == 0)
        {
            return;
        }

        var action = await Ui.ChooseResubmitActionAsync(selected.Count, target).ConfigureAwait(true);
        if (action is null)
        {
            return;
        }

        var removeOriginals = action == ResubmitAction.ResubmitAndDelete;
        var origin = SelectedNode;

        await RunAsync(
            OperationKind.Send,
            $"Resubmitting {selected.Count} message(s) to {target}…",
            async cancellationToken =>
        {
            var sent = await messages.ResubmitAsync(
                target,
                selected.Select(row => row.Record).ToList(),
                removeOriginals,
                path,
                cancellationToken).ConfigureAwait(true);

            AppendLog(removeOriginals
                ? $"Resubmitted {sent} message(s) to {target} and removed the dead-letter copies."
                : $"Resubmitted {sent} message(s) to {target}.");

            await RefreshNodeAsync(origin, cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task DeleteSelectedMessagesAsync()
    {
        var path = CurrentPath;
        var messages = SelectedNamespace?.Messages;
        var selected = SelectedMessages.ToList();

        if (path is null || messages is null || Ui is null || selected.Count == 0)
        {
            return;
        }

        var deadLetter = ShowDeadLetter;

        var confirmed = await Ui.ConfirmAsync(
            "Delete messages",
            $"Permanently delete {selected.Count:N0} selected message(s) from " +
            $"{path}{(deadLetter ? "/$DeadLetterQueue" : string.Empty)}?\n\n" +
            "Other messages in the entity are briefly locked and released while the targets are found, " +
            "which increases their delivery count.",
            "Delete",
            destructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        var origin = SelectedNode;

        await RunAsync(
            OperationKind.Delete,
            $"Deleting {selected.Count} message(s) from {path}…",
            async cancellationToken =>
        {
            var deleted = await messages.DeleteMessagesAsync(
                path,
                deadLetter,
                selected.Select(row => row.SequenceNumber).ToHashSet(),
                cancellationToken).ConfigureAwait(true);

            AppendLog($"Deleted {deleted} of {selected.Count} selected message(s) from {path}.");

            if (deleted < selected.Count)
            {
                AppendLog("Some messages were not found — they may have been consumed or locked by another receiver.");
            }

            // Only touch the grid if it is still showing the entity these came from.
            if (ReferenceEquals(SelectedNode, origin))
            {
                foreach (var row in selected)
                {
                    Messages.Remove(row);
                }
            }

            await RefreshNodeAsync(origin, cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task PurgeAsync()
    {
        var path = CurrentPath;
        var messages = SelectedNamespace?.Messages;
        if (path is null || messages is null || Ui is null)
        {
            return;
        }

        var deadLetter = ShowDeadLetter;
        var label = deadLetter ? $"{path}/$DeadLetterQueue" : path.ToString();

        var confirmed = await Ui.ConfirmAsync(
            "Purge",
            $"Permanently delete every message in {label}?\n\nThis cannot be undone.",
            "Purge",
            destructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        // The counts on the selected node are a moment old and a live entity keeps
        // filling, so this drives the progress bar but never the stopping condition.
        var expected = ExpectedMessageCount(SelectedNode, deadLetter);
        var origin = SelectedNode;

        await RunAsync(OperationKind.Purge, $"Purging {label}…", async (operation, cancellationToken) =>
        {
            var progress = new Progress<PurgeProgress>(report =>
                operation.Report($"{report.DeletedCount:N0} deleted", report.DeletedCount, expected));

            var deleted = await messages
                .PurgeAsync(path, deadLetter, progress, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            AppendLog($"Purged {deleted:N0} message(s) from {label}.");

            if (ReferenceEquals(SelectedNode, origin))
            {
                Messages.Clear();
            }

            await RefreshNodeAsync(origin, cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>
    /// How many messages a purge is expected to remove, for the progress bar. Null when the
    /// selection doesn't carry a count.
    /// </summary>
    static long? ExpectedMessageCount(TreeNodeViewModel? node, bool deadLetter) => node switch
    {
        QueueNodeViewModel queue =>
            deadLetter ? queue.Entity.DeadLetterMessageCount : queue.Entity.ActiveMessageCount,

        SubscriptionNodeViewModel subscription =>
            deadLetter ? subscription.Entity.DeadLetterMessageCount : subscription.Entity.ActiveMessageCount,

        _ => null
    };

    [RelayCommand]
    async Task CopyBodyAsync()
    {
        if (SelectedMessage is null || Ui is null)
        {
            return;
        }

        await Ui.SetClipboardTextAsync(SelectedMessage.BodyText).ConfigureAwait(true);
        StatusText = "Body copied to the clipboard.";
    }

    [RelayCommand]
    async Task SaveMessagesAsync()
    {
        if (Ui is null || Messages.Count == 0)
        {
            return;
        }

        var rows = SelectedMessages.Count > 0 ? SelectedMessages.ToList() : Messages.ToList();

        var path = await Ui.PickSaveFileAsync(new FilePickerRequest(
            "Save messages",
            $"messages-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            "json")).ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        await RunAsync(OperationKind.Transfer, $"Saving {rows.Count} message(s)…", async _ =>
        {
            var builder = new StringBuilder();
            builder.Append("[\n");

            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                builder.Append("  {\n");
                builder.Append($"    \"sequenceNumber\": {row.SequenceNumber},\n");
                builder.Append($"    \"messageId\": {System.Text.Json.JsonSerializer.Serialize(row.MessageId)},\n");
                builder.Append($"    \"enqueuedTime\": {System.Text.Json.JsonSerializer.Serialize(row.EnqueuedTime)},\n");
                builder.Append($"    \"bodyFormat\": {System.Text.Json.JsonSerializer.Serialize(row.BodyFormatLabel)},\n");
                builder.Append($"    \"body\": {System.Text.Json.JsonSerializer.Serialize(row.BodyText)},\n");
                builder.Append($"    \"properties\": {System.Text.Json.JsonSerializer.Serialize(row.PropertiesText)}\n");
                builder.Append(index == rows.Count - 1 ? "  }\n" : "  },\n");
            }

            builder.Append("]\n");

            await File.WriteAllTextAsync(path, builder.ToString(), Encoding.UTF8).ConfigureAwait(true);
            AppendLog($"Saved {rows.Count} message(s) to {path}.");
        }).ConfigureAwait(true);
    }

    // -------------------------------------------------------- entity commands

    [RelayCommand]
    async Task CreateQueueAsync()
    {
        var node = SelectedNamespace;
        if (node?.Entities is null || Ui is null)
        {
            return;
        }

        var definition = await Ui
            .EditQueueAsync(new QueueDefinition { Name = string.Empty }, isNew: true)
            .ConfigureAwait(true);

        if (definition is null)
        {
            return;
        }

        await RunAsync(OperationKind.Manage, $"Creating queue {definition.Name}…", async cancellationToken =>
        {
            await node.Entities.CreateQueueAsync(
                ImportExportService.ToCreateOptions(definition), cancellationToken).ConfigureAwait(true);

            AppendLog($"Created queue “{definition.Name}”.");

            if (node.QueueFolder is not null)
            {
                await node.QueueFolder.RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task CreateTopicAsync()
    {
        var node = SelectedNamespace;
        if (node?.Entities is null || Ui is null)
        {
            return;
        }

        var definition = await Ui
            .EditTopicAsync(new TopicDefinition { Name = string.Empty }, isNew: true)
            .ConfigureAwait(true);

        if (definition is null)
        {
            return;
        }

        await RunAsync(OperationKind.Manage, $"Creating topic {definition.Name}…", async cancellationToken =>
        {
            await node.Entities.CreateTopicAsync(
                ImportExportService.ToCreateOptions(definition), cancellationToken).ConfigureAwait(true);

            AppendLog($"Created topic “{definition.Name}”.");

            if (node.TopicFolder is not null)
            {
                await node.TopicFolder.RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task CreateSubscriptionAsync()
    {
        var topicNode = SelectedTopic;
        var entities = SelectedNamespace?.Entities;
        if (topicNode is null || entities is null || Ui is null)
        {
            return;
        }

        var topicName = topicNode.Entity.Name;

        var definition = await Ui
            .EditSubscriptionAsync(topicName, new SubscriptionDefinition { Name = string.Empty }, isNew: true)
            .ConfigureAwait(true);

        if (definition is null)
        {
            return;
        }

        await RunAsync(OperationKind.Manage, $"Creating subscription {topicName}/{definition.Name}…", async cancellationToken =>
        {
            var options = ImportExportService.ToCreateOptions(topicName, definition);
            var defaultRule = definition.Rules.FirstOrDefault();

            if (defaultRule is not null)
            {
                await entities.CreateSubscriptionAsync(
                    options,
                    ImportExportService.ToCreateOptions(defaultRule),
                    cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await entities.CreateSubscriptionAsync(options, cancellationToken).ConfigureAwait(true);
            }

            AppendLog($"Created subscription “{topicName}/{definition.Name}”.");
            await topicNode.RefreshAsync(cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task EditEntityAsync()
    {
        var entities = SelectedNamespace?.Entities;
        if (entities is null || Ui is null)
        {
            return;
        }

        if (SelectedQueue is { } queueNode)
        {
            var edited = await Ui
                .EditQueueAsync(ImportExportService.ToDefinition(queueNode.Entity.Properties), isNew: false)
                .ConfigureAwait(true);

            if (edited is null)
            {
                return;
            }

            await RunAsync(OperationKind.Manage, $"Updating queue {edited.Name}…", async cancellationToken =>
            {
                // Start from the live properties so create-only settings keep the values
                // the service already has rather than whatever the form defaulted to.
                var properties = (await entities.GetQueueAsync(queueNode.Entity.Name, cancellationToken)
                    .ConfigureAwait(true)).Properties;

                ApplyQueueEdits(edited, properties);
                await entities.UpdateQueueAsync(properties, cancellationToken).ConfigureAwait(true);

                AppendLog($"Updated queue “{edited.Name}”.");
                await queueNode.RefreshAsync(cancellationToken).ConfigureAwait(true);
                RaiseEntityPropertiesChanged();
            }).ConfigureAwait(true);

            return;
        }

        if (SelectedTopic is { } topicNode)
        {
            var edited = await Ui
                .EditTopicAsync(ImportExportService.ToDefinition(topicNode.Entity.Properties), isNew: false)
                .ConfigureAwait(true);

            if (edited is null)
            {
                return;
            }

            await RunAsync(OperationKind.Manage, $"Updating topic {edited.Name}…", async cancellationToken =>
            {
                var properties = (await entities.GetTopicAsync(topicNode.Entity.Name, cancellationToken)
                    .ConfigureAwait(true)).Properties;

                ApplyTopicEdits(edited, properties);
                await entities.UpdateTopicAsync(properties, cancellationToken).ConfigureAwait(true);

                AppendLog($"Updated topic “{edited.Name}”.");
                await topicNode.RefreshAsync(cancellationToken).ConfigureAwait(true);
                RaiseEntityPropertiesChanged();
            }).ConfigureAwait(true);

            return;
        }

        if (SelectedSubscription is { } subscriptionNode)
        {
            var topicName = subscriptionNode.Entity.TopicName;

            var edited = await Ui.EditSubscriptionAsync(
                topicName,
                ImportExportService.ToDefinition(subscriptionNode.Entity.Properties),
                isNew: false).ConfigureAwait(true);

            if (edited is null)
            {
                return;
            }

            await RunAsync(OperationKind.Manage, $"Updating subscription {topicName}/{edited.Name}…", async cancellationToken =>
            {
                var properties = (await entities
                    .GetSubscriptionAsync(topicName, subscriptionNode.Entity.Name, cancellationToken)
                    .ConfigureAwait(true)).Properties;

                ApplySubscriptionEdits(edited, properties);
                await entities.UpdateSubscriptionAsync(properties, cancellationToken).ConfigureAwait(true);

                AppendLog($"Updated subscription “{topicName}/{edited.Name}”.");
                await subscriptionNode.RefreshAsync(cancellationToken).ConfigureAwait(true);
                RaiseEntityPropertiesChanged();
            }).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    async Task DeleteEntityAsync()
    {
        if (SelectedNamespace is not { Entities: { } entities } namespaceNode || Ui is null)
        {
            return;
        }

        (string Description, Func<CancellationToken, Task> Delete, Func<CancellationToken, Task> Refresh)? target =
            SelectedNode switch
            {
                QueueNodeViewModel queue => (
                    $"queue “{queue.Entity.Name}”",
                    token => entities.DeleteQueueAsync(queue.Entity.Name, token),
                    token => namespaceNode.QueueFolder?.RefreshAsync(token) ?? Task.CompletedTask),

                TopicNodeViewModel topic => (
                    $"topic “{topic.Entity.Name}” and all of its subscriptions",
                    token => entities.DeleteTopicAsync(topic.Entity.Name, token),
                    token => namespaceNode.TopicFolder?.RefreshAsync(token) ?? Task.CompletedTask),

                SubscriptionNodeViewModel subscription => (
                    $"subscription “{subscription.Entity.TopicName}/{subscription.Entity.Name}”",
                    token => entities.DeleteSubscriptionAsync(
                        subscription.Entity.TopicName, subscription.Entity.Name, token),
                    token => (subscription.Parent as TopicNodeViewModel)?.RefreshAsync(token) ?? Task.CompletedTask),

                _ => null
            };

        if (target is null)
        {
            return;
        }

        var (description, deleteAsync, refreshAsync) = target.Value;

        var confirmed = await Ui.ConfirmAsync(
            "Delete entity",
            $"Permanently delete the {description}?\n\nEvery message it holds is deleted too. This cannot be undone.",
            "Delete",
            destructive: true).ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await RunAsync(OperationKind.Delete, $"Deleting {description}…", async cancellationToken =>
        {
            await deleteAsync(cancellationToken).ConfigureAwait(true);
            AppendLog($"Deleted {description}.");

            SelectedNode = null;
            Messages.Clear();

            await refreshAsync(cancellationToken).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    /// <summary>Toggles the selected entity between Active and Disabled.</summary>
    [RelayCommand]
    async Task ToggleEntityStatusAsync()
    {
        var entities = SelectedNamespace?.Entities;
        if (entities is null)
        {
            return;
        }

        var node = SelectedNode;

        (string Description, Func<CancellationToken, Task> Apply)? target = node switch
        {
            QueueNodeViewModel queue => (
                $"queue “{queue.Entity.Name}”",
                token => entities.SetQueueStatusAsync(queue.Entity.Name, NextStatus(queue.IsDisabled), token)),

            TopicNodeViewModel topic => (
                $"topic “{topic.Entity.Name}”",
                token => entities.SetTopicStatusAsync(topic.Entity.Name, NextStatus(topic.IsDisabled), token)),

            SubscriptionNodeViewModel subscription => (
                $"subscription “{subscription.Entity.Name}”",
                token => entities.SetSubscriptionStatusAsync(
                    subscription.Entity.TopicName, subscription.Entity.Name, NextStatus(subscription.IsDisabled), token)),

            _ => null
        };

        if (target is null || node is null)
        {
            return;
        }

        var (description, apply) = target.Value;
        var enabling = node.IsDisabled;

        await RunAsync(OperationKind.Manage, $"{(enabling ? "Enabling" : "Disabling")} {description}…", async cancellationToken =>
        {
            await apply(cancellationToken).ConfigureAwait(true);
            await node.RefreshAsync(cancellationToken).ConfigureAwait(true);
            RaiseEntityPropertiesChanged();
            AppendLog($"{(enabling ? "Enabled" : "Disabled")} {description}.");
        }).ConfigureAwait(true);

        static Azure.Messaging.ServiceBus.Administration.EntityStatus NextStatus(bool isCurrentlyDisabled) =>
            isCurrentlyDisabled
                ? Azure.Messaging.ServiceBus.Administration.EntityStatus.Active
                : Azure.Messaging.ServiceBus.Administration.EntityStatus.Disabled;
    }

    // ------------------------------------------------------- import / export

    [RelayCommand]
    async Task ExportEntitiesAsync()
    {
        var node = SelectedNamespace;
        if (node?.ImportExport is null || Ui is null)
        {
            return;
        }

        var suggested = $"{node.Connection.ResolvedNamespace.Split('.')[0]}-entities.xml";
        var path = await Ui
            .PickSaveFileAsync(new FilePickerRequest("Export entity definitions", suggested, "xml", "json"))
            .ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        await RunAsync(OperationKind.Transfer, $"Exporting {node.Title} entity definitions…", async cancellationToken =>
        {
            var definition = await node.ImportExport
                .ExportAsync(node.Connection.ResolvedNamespace, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

            var format = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? DefinitionFormat.Json
                : DefinitionFormat.Xml;

            await node.ImportExport.WriteAsync(definition, path, format, cancellationToken).ConfigureAwait(true);

            var subscriptionCount = definition.Topics.Sum(topic => topic.Subscriptions.Count);
            AppendLog(
                $"Exported {definition.Queues.Count} queue(s), {definition.Topics.Count} topic(s) and " +
                $"{subscriptionCount} subscription(s) to {path}.");
        }).ConfigureAwait(true);
    }

    [RelayCommand]
    async Task ImportEntitiesAsync()
    {
        var node = SelectedNamespace;
        if (node?.ImportExport is null || Ui is null)
        {
            return;
        }

        var path = await Ui
            .PickOpenFileAsync(new FilePickerRequest("Import entity definitions", null, "xml", "json"))
            .ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        NamespaceDefinition definition;
        try
        {
            definition = await ImportExportService.ReadAsync(path).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            await Ui.ShowErrorAsync("Import failed", $"Could not read {path}.\n\n{exception.Message}").ConfigureAwait(true);
            return;
        }

        var policy = await Ui.ChooseImportPolicyAsync(definition).ConfigureAwait(true);
        if (policy is null)
        {
            return;
        }

        // Every queue, topic and subscription in the file is one step, so the bar can fill
        // rather than sweep.
        var steps = definition.Queues.Count +
            definition.Topics.Count +
            definition.Topics.Sum(topic => topic.Subscriptions.Count);

        await RunAsync(
            OperationKind.Transfer,
            $"Importing entity definitions into {node.Title}…",
            async (operation, cancellationToken) =>
        {
            var completed = 0L;

            var progress = new Progress<ImportStep>(step =>
                operation.Report($"{step.Action} {step.EntityPath}", ++completed, steps));

            var result = await node.ImportExport
                .ImportAsync(definition, policy.Value, progress, cancellationToken)
                .ConfigureAwait(true);

            AppendLog(
                $"Import finished: {result.CreatedCount} created, {result.UpdatedCount} updated, " +
                $"{result.SkippedCount} skipped, {result.FailedCount} failed.");

            await node.RefreshAsync(cancellationToken).ConfigureAwait(true);
            await Ui.ShowImportResultAsync(result).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    // ------------------------------------------------------------- internals

    [RelayCommand]
    void ToggleActivity() => IsActivityExpanded = !IsActivityExpanded;

    [RelayCommand]
    void CancelAllOperations()
    {
        foreach (var operation in Operations.Where(operation => operation.IsRunning).ToList())
        {
            operation.Cancel();
        }

        StatusText = "Cancelling…";
    }

    /// <summary>Drops the finished entries, leaving whatever is still running.</summary>
    [RelayCommand]
    void ClearFinishedOperations()
    {
        foreach (var operation in Operations.Where(operation => operation.IsFinished).ToList())
        {
            Operations.Remove(operation);
        }

        if (Operations.Count == 0)
        {
            IsActivityExpanded = false;
        }
    }

    [RelayCommand]
    void ClearLog() => Log.Clear();

    /// <summary>
    /// Runs an operation as its own tracked activity, funnelling any failure into the log
    /// and an error dialog rather than letting it reach the dispatcher.
    /// </summary>
    Task RunAsync(OperationKind kind, string description, Func<CancellationToken, Task> operation) =>
        RunAsync(kind, description, (_, cancellationToken) => operation(cancellationToken));

    /// <summary>
    /// As above, but hands the operation its own tray entry so it can report progress
    /// against it.
    /// </summary>
    /// <remarks>
    /// Several of these can be in flight at once — that is the point, so that browsing an
    /// entity that has already loaded isn't blocked by a namespace still enumerating its
    /// topics. Everything here runs on the UI thread and only interleaves at await points,
    /// so the bound collections need no locking.
    /// </remarks>
    async Task RunAsync(
        OperationKind kind,
        string description,
        Func<OperationViewModel, CancellationToken, Task> operation)
    {
        if (!activeOperations.Add(description))
        {
            // The same request is already running — a double-tapped shortcut, usually.
            // Ignoring it is friendlier than queueing a duplicate.
            return;
        }

        var tracked = new OperationViewModel(kind, description);

        TrimFinishedOperations();
        Operations.Add(tracked);
        tracked.PropertyChanged += OnOperationChanged;

        StatusText = description;

        try
        {
            await operation(tracked, tracked.Token).ConfigureAwait(true);

            tracked.Finish(OperationState.Completed);
            StatusText = "Ready";
        }
        catch (OperationCanceledException)
        {
            tracked.Finish(OperationState.Cancelled);
            StatusText = "Cancelled";
            AppendLog($"Cancelled: {description}");
        }
        catch (Exception exception)
        {
            tracked.Finish(OperationState.Failed, exception.Message);
            StatusText = "Failed";
            AppendLog($"Error: {exception.Message}");

            if (Ui is not null)
            {
                await Ui.ShowErrorAsync("Operation failed", exception.Message).ConfigureAwait(true);
            }
        }
        finally
        {
            // Its state is settled by now, so the subscription has nothing left to report.
            tracked.PropertyChanged -= OnOperationChanged;

            activeOperations.Remove(description);
            RaiseActivityChanged();
        }
    }

    void OnOperationChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(OperationViewModel.State))
        {
            RaiseActivityChanged();
        }
    }

    /// <summary>
    /// Keeps the last few finished entries so it stays visible what just completed, without
    /// letting the tray grow for the length of a session.
    /// </summary>
    void TrimFinishedOperations()
    {
        var finished = Operations.Where(operation => operation.IsFinished).ToList();

        for (var index = 0; index <= finished.Count - FinishedOperationsKept; index++)
        {
            Operations.Remove(finished[index]);
        }
    }

    void RaiseActivityChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(HasOperations));
        OnPropertyChanged(nameof(HasFinishedOperations));
        OnPropertyChanged(nameof(RunningOperationCount));
        OnPropertyChanged(nameof(ActivitySummary));
    }

    /// <summary>
    /// Puts freshly read messages in the grid, but only while the selection they came from
    /// is still the one on screen. Reads now run alongside each other, so a slow one can
    /// finish after the user has moved on — and dropping those results is better than
    /// showing one entity's messages under another's name.
    /// </summary>
    void ShowMessagesFor(TreeNodeViewModel? origin, IReadOnlyList<MessageRecord> records, string source)
    {
        if (!ReferenceEquals(SelectedNode, origin))
        {
            AppendLog($"Discarded {records.Count} message(s) read from {source} — the selection moved on.");
            return;
        }

        Messages.Clear();
        SelectedMessages.Clear();

        foreach (var record in records)
        {
            Messages.Add(new MessageRowViewModel(record));
        }

        SelectedMessage = Messages.FirstOrDefault();
    }

    /// <summary>
    /// Reloads the node an operation acted on, whether or not it is still selected — its
    /// counts changed either way.
    /// </summary>
    async Task RefreshNodeAsync(TreeNodeViewModel? node, CancellationToken cancellationToken)
    {
        if (node is null)
        {
            return;
        }

        await node.RefreshAsync(cancellationToken).ConfigureAwait(true);

        if (ReferenceEquals(SelectedNode, node))
        {
            RaiseEntityPropertiesChanged();
        }
    }

    void RaiseEntityPropertiesChanged()
    {
        OnPropertyChanged(nameof(EntityHeader));
        OnPropertyChanged(nameof(EntitySummary));
    }

    void AppendLog(string message)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");

        // Keep the log bounded; a long purge can otherwise grow it without limit.
        while (Log.Count > 500)
        {
            Log.RemoveAt(Log.Count - 1);
        }
    }

    static void ApplyQueueEdits(QueueDefinition edited, Azure.Messaging.ServiceBus.Administration.QueueProperties target)
    {
        target.MaxDeliveryCount = edited.MaxDeliveryCount;
        target.DeadLetteringOnMessageExpiration = edited.DeadLetteringOnMessageExpiration;
        target.EnableBatchedOperations = edited.EnableBatchedOperations;
        target.UserMetadata = ImportExportService.EmptyIfBlank(edited.UserMetadata);
        target.ForwardTo = edited.ForwardTo;
        target.ForwardDeadLetteredMessagesTo = edited.ForwardDeadLetteredMessagesTo;
        target.Status = ParseStatus(edited.Status);

        if (edited.MaxSizeInMegabytes is { } maxSize)
        {
            target.MaxSizeInMegabytes = maxSize;
        }

        if (DurationText.Parse(DurationText.ToDisplay(edited.LockDuration)) is { } lockDuration)
        {
            target.LockDuration = lockDuration;
        }

        if (DurationText.Parse(DurationText.ToDisplay(edited.DefaultMessageTimeToLive)) is { } timeToLive)
        {
            target.DefaultMessageTimeToLive = timeToLive;
        }

        if (DurationText.Parse(DurationText.ToDisplay(edited.AutoDeleteOnIdle)) is { } autoDelete)
        {
            target.AutoDeleteOnIdle = autoDelete;
        }
    }

    static void ApplyTopicEdits(TopicDefinition edited, Azure.Messaging.ServiceBus.Administration.TopicProperties target)
    {
        target.EnableBatchedOperations = edited.EnableBatchedOperations;
        target.UserMetadata = ImportExportService.EmptyIfBlank(edited.UserMetadata);
        target.Status = ParseStatus(edited.Status);

        if (edited.MaxSizeInMegabytes is { } maxSize)
        {
            target.MaxSizeInMegabytes = maxSize;
        }

        if (DurationText.Parse(DurationText.ToDisplay(edited.DefaultMessageTimeToLive)) is { } timeToLive)
        {
            target.DefaultMessageTimeToLive = timeToLive;
        }

        if (DurationText.Parse(DurationText.ToDisplay(edited.AutoDeleteOnIdle)) is { } autoDelete)
        {
            target.AutoDeleteOnIdle = autoDelete;
        }
    }

    static void ApplySubscriptionEdits(
        SubscriptionDefinition edited,
        Azure.Messaging.ServiceBus.Administration.SubscriptionProperties target)
    {
        target.MaxDeliveryCount = edited.MaxDeliveryCount;
        target.DeadLetteringOnMessageExpiration = edited.DeadLetteringOnMessageExpiration;
        target.EnableDeadLetteringOnFilterEvaluationExceptions = edited.EnableDeadLetteringOnFilterEvaluationExceptions;
        target.EnableBatchedOperations = edited.EnableBatchedOperations;
        target.UserMetadata = ImportExportService.EmptyIfBlank(edited.UserMetadata);
        target.ForwardTo = edited.ForwardTo;
        target.ForwardDeadLetteredMessagesTo = edited.ForwardDeadLetteredMessagesTo;
        target.Status = ParseStatus(edited.Status);

        if (DurationText.Parse(DurationText.ToDisplay(edited.LockDuration)) is { } lockDuration)
        {
            target.LockDuration = lockDuration;
        }

        if (DurationText.Parse(DurationText.ToDisplay(edited.DefaultMessageTimeToLive)) is { } timeToLive)
        {
            target.DefaultMessageTimeToLive = timeToLive;
        }

        if (DurationText.Parse(DurationText.ToDisplay(edited.AutoDeleteOnIdle)) is { } autoDelete)
        {
            target.AutoDeleteOnIdle = autoDelete;
        }
    }

    // EntityStatus is a struct, not an enum, so it needs the core service's parser rather
    // than Enum.TryParse — see ImportExportService.ParseStatus.
    static Azure.Messaging.ServiceBus.Administration.EntityStatus ParseStatus(string? value) =>
        ImportExportService.ParseStatus(value);
}
