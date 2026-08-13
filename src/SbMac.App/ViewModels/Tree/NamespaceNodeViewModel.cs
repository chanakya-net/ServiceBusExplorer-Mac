using CommunityToolkit.Mvvm.ComponentModel;

using SbMac.Core;
using SbMac.Core.Connections;
using SbMac.Core.Entities;
using SbMac.Core.ImportExport;
using SbMac.Core.Messaging;

namespace SbMac.App.ViewModels.Tree;

/// <summary>
/// A saved namespace and, once connected, the services bound to it. This is the root
/// of each branch in the tree and the owner of the live <see cref="ServiceBusSession"/>.
/// </summary>
public sealed partial class NamespaceNodeViewModel : TreeNodeViewModel, IAsyncDisposable
{
    ServiceBusSession? session;

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

    public QueueFolderNodeViewModel? QueueFolder { get; private set; }

    public TopicFolderNodeViewModel? TopicFolder { get; private set; }

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

        Detail = Connection.AuthenticationMode == AuthenticationMode.EntraId ? "Entra ID" : null;
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

            await QueueFolder.RefreshAsync(cancellationToken).ConfigureAwait(true);
            await TopicFolder.RefreshAsync(cancellationToken).ConfigureAwait(true);
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

    public async Task DisconnectAsync()
    {
        if (session is not null)
        {
            await session.DisposeAsync().ConfigureAwait(true);
            session = null;
        }

        Entities = null;
        Messages = null;
        ImportExport = null;
        QueueFolder = null;
        TopicFolder = null;

        Children.Clear();
        IsConnected = false;
    }

    /// <summary>Reloads both folders. Does nothing when not connected.</summary>
    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (QueueFolder is not null)
            {
                await QueueFolder.RefreshAsync(cancellationToken).ConfigureAwait(true);
            }

            if (TopicFolder is not null)
            {
                await TopicFolder.RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
