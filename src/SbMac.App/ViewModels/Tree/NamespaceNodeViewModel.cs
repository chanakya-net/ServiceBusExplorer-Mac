using CommunityToolkit.Mvvm.ComponentModel;

using SbMac.Core;
using SbMac.Core.Connections;
using SbMac.Core.Entities;
using SbMac.Core.EventHubs;
using SbMac.Core.ImportExport;
using SbMac.Core.Messaging;

namespace SbMac.App.ViewModels.Tree;

/// <summary>
/// A saved namespace and, once connected, the services bound to it. This is the root
/// of each branch in the tree and the owner of the live session — a
/// <see cref="ServiceBusSession"/> or an <see cref="EventHubsSession"/>, depending on
/// which service the connection points at.
/// </summary>
public sealed partial class NamespaceNodeViewModel : TreeNodeViewModel, IAsyncDisposable
{
    ServiceBusSession? session;
    EventHubsSession? eventHubsSession;

    public NamespaceNodeViewModel(NamespaceConnection connection)
    {
        Connection = connection;
        Icon = "IconNamespace";
        UpdateTitle();
    }

    public NamespaceConnection Connection { get; private set; }

    [ObservableProperty]
    bool isConnected;

    /// <summary>Set when the last connection attempt failed, so the tree can show why.</summary>
    [ObservableProperty]
    string? connectionError;

    public EntityService? Entities { get; private set; }

    public MessageService? Messages { get; private set; }

    public ImportExportService? ImportExport { get; private set; }

    /// <summary>Set instead of <see cref="Entities"/> when this is an Event Hubs namespace.</summary>
    public EventHubService? EventHubs { get; private set; }

    public QueueFolderNodeViewModel? QueueFolder { get; private set; }

    public TopicFolderNodeViewModel? TopicFolder { get; private set; }

    public EventHubFolderNodeViewModel? EventHubFolder { get; private set; }

    /// <summary>True when this namespace is browsed as Event Hubs rather than Service Bus.</summary>
    public bool IsEventHubs => Connection.Kind == NamespaceKind.EventHubs;

    /// <summary>Replaces the saved settings, e.g. after the user edits the connection.</summary>
    public void UpdateConnection(NamespaceConnection connection)
    {
        Connection = connection;
        UpdateTitle();
    }

    void UpdateTitle()
    {
        Title = string.IsNullOrWhiteSpace(Connection.Name)
            ? Connection.ResolvedNamespace
            : Connection.Name;

        // Two facts fight for one line here, so only the non-default ones are shown:
        // Service Bus is the assumed service, and a SAS key the assumed credential.
        var parts = new List<string>(2);
        if (Connection.Kind == NamespaceKind.EventHubs)
        {
            parts.Add("Event Hubs");
        }

        if (Connection.AuthenticationMode == AuthenticationMode.EntraId)
        {
            parts.Add("Entra ID");
        }

        Detail = parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>
    /// Opens a session and loads the top-level folders. Safe to call again to reconnect;
    /// any previous session is disposed first.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(true);

        IsBusy = true;
        ConnectionError = null;

        try
        {
            if (IsEventHubs)
            {
                await ConnectEventHubsAsync(cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await ConnectServiceBusAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            // Leave the node in the tree with the failure attached — the user usually
            // wants to edit the connection and retry rather than lose the entry.
            ConnectionError = exception.Message;
            IsConnected = false;
            await DisconnectAsync().ConfigureAwait(true);
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    async Task ConnectServiceBusAsync(CancellationToken cancellationToken)
    {
        session = ServiceBusSession.Create(Connection);
        await session.TestConnectionAsync(cancellationToken).ConfigureAwait(true);

        Entities = new EntityService(session);
        Messages = new MessageService(session);
        ImportExport = new ImportExportService(Entities);

        QueueFolder = new QueueFolderNodeViewModel { Parent = this };
        TopicFolder = new TopicFolderNodeViewModel { Parent = this };

        Children.Clear();
        Children.Add(QueueFolder);
        Children.Add(TopicFolder);

        IsConnected = true;
        IsExpanded = true;

        // Queues and topics are independent listings; waiting for one before starting the
        // other doubled how long the tree took to appear.
        await RefreshAllAsync([QueueFolder, TopicFolder], cancellationToken).ConfigureAwait(true);
    }

    async Task ConnectEventHubsAsync(CancellationToken cancellationToken)
    {
        eventHubsSession = EventHubsSession.Create(Connection);
        await eventHubsSession.TestConnectionAsync(cancellationToken).ConfigureAwait(true);

        EventHubs = new EventHubService(eventHubsSession);

        EventHubFolder = new EventHubFolderNodeViewModel { Parent = this };

        Children.Clear();
        Children.Add(EventHubFolder);

        IsConnected = true;
        IsExpanded = true;

        await EventHubFolder.RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    public async Task DisconnectAsync()
    {
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(true);
            session = null;
        }

        if (eventHubsSession is not null)
        {
            await eventHubsSession.DisposeAsync().ConfigureAwait(true);
            eventHubsSession = null;
        }

        Entities = null;
        Messages = null;
        ImportExport = null;
        EventHubs = null;
        QueueFolder = null;
        TopicFolder = null;
        EventHubFolder = null;

        Children.Clear();
        IsConnected = false;
    }

    /// <summary>Reloads the top-level folders. Does nothing when not connected.</summary>
    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        IsBusy = true;
        try
        {
            TreeNodeViewModel?[] folders = [QueueFolder, TopicFolder, EventHubFolder];

            await RefreshAllAsync(folders.OfType<TreeNodeViewModel>().ToList(), cancellationToken)
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
