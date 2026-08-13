using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using SbMac.Core.Connections;

namespace SbMac.App.ViewModels.Dialogs;

/// <summary>Backs the add/edit namespace dialog.</summary>
public sealed partial class ConnectionDialogViewModel : ViewModelBase
{
    readonly Guid id;

    public ConnectionDialogViewModel(NamespaceConnection? existing)
    {
        IsNew = existing is null;

        var connection = existing ?? new NamespaceConnection();
        id = connection.Id;

        name = connection.Name;
        useEventHubs = connection.Kind == NamespaceKind.EventHubs;
        useEntraId = connection.AuthenticationMode == AuthenticationMode.EntraId;
        connectionString = connection.ConnectionString ?? string.Empty;
        fullyQualifiedNamespace = connection.FullyQualifiedNamespace ?? string.Empty;
        tenantId = connection.TenantId ?? string.Empty;
        clientId = connection.ClientId ?? string.Empty;
        clientSecret = connection.ClientSecret ?? string.Empty;
        useWebSockets = connection.Transport == ServiceBusTransport.AmqpWebSockets;
        selectedCredentialKind = connection.EntraCredentialKind;
        eventHubNames = string.Join(Environment.NewLine, connection.EventHubNames);
        consumerGroup = connection.ConsumerGroup ?? string.Empty;
        subscriptionId = connection.SubscriptionId ?? string.Empty;
    }

    public bool IsNew { get; }

    public string DialogTitle => IsNew ? "Add namespace" : "Edit namespace";

    public IReadOnlyList<EntraCredentialKind> CredentialKinds { get; } =
        Enum.GetValues<EntraCredentialKind>();

    /// <summary>Labels shown in the credential dropdown, in the same order as <see cref="CredentialKinds"/>.</summary>
    public ObservableCollection<string> CredentialKindLabels { get; } =
        new(Enum.GetValues<EntraCredentialKind>().Select(CredentialFactory.Describe));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEventHubFields))]
    [NotifyPropertyChangedFor(nameof(ShowSubscriptionField))]
    [NotifyPropertyChangedFor(nameof(ConnectionStringPlaceholder))]
    [NotifyPropertyChangedFor(nameof(RoleHint))]
    bool useEventHubs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ShowConnectionStringFields))]
    [NotifyPropertyChangedFor(nameof(ShowEntraFields))]
    [NotifyPropertyChangedFor(nameof(ShowSubscriptionField))]
    [NotifyPropertyChangedFor(nameof(RoleHint))]
    bool useEntraId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string connectionString;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string fullyQualifiedNamespace;

    [ObservableProperty]
    string tenantId;

    [ObservableProperty]
    string clientId;

    [ObservableProperty]
    string clientSecret;

    [ObservableProperty]
    bool useWebSockets;

    /// <summary>
    /// Event hub names, one per line. Free text rather than a picker because the hubs in a
    /// namespace can only be listed through ARM, which not every identity can reach.
    /// </summary>
    [ObservableProperty]
    string eventHubNames;

    [ObservableProperty]
    string consumerGroup;

    [ObservableProperty]
    string subscriptionId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ShowClientSecretFields))]
    EntraCredentialKind selectedCredentialKind;

    /// <summary>Bound to the dropdown's SelectedIndex, since the labels are plain strings.</summary>
    public int SelectedCredentialIndex
    {
        get => Array.IndexOf(Enum.GetValues<EntraCredentialKind>(), SelectedCredentialKind);
        set
        {
            var kinds = Enum.GetValues<EntraCredentialKind>();
            if (value >= 0 && value < kinds.Length)
            {
                SelectedCredentialKind = kinds[value];
            }
        }
    }

    [ObservableProperty]
    string? validationError;

    public bool ShowConnectionStringFields => !UseEntraId;

    public bool ShowEntraFields => UseEntraId;

    public bool ShowClientSecretFields => UseEntraId && SelectedCredentialKind == EntraCredentialKind.ClientSecret;

    public bool ShowEventHubFields => UseEventHubs;

    /// <summary>
    /// The subscription only matters for ARM hub discovery, which needs an Entra identity.
    /// </summary>
    public bool ShowSubscriptionField => UseEventHubs && UseEntraId;

    /// <summary>
    /// An Event Hubs connection string copied from a hub rather than the namespace carries
    /// an <c>EntityPath</c>, which pins it to that one hub — worth showing so the shape is
    /// recognisable when it happens.
    /// </summary>
    public string ConnectionStringPlaceholder => UseEventHubs
        ? "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=…;SharedAccessKey=…;EntityPath=telemetry"
        : "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=…;SharedAccessKey=…";

    /// <summary>The role the signed-in identity needs, which differs between the two services.</summary>
    public string RoleHint => UseEventHubs
        ? "Your identity needs the Azure Event Hubs Data Receiver role to read events, and Data Sender to publish them. " +
          "Listing the hubs in the namespace additionally needs Reader on the namespace resource; without it, name the hubs below."
        : "Your identity needs the Azure Service Bus Data Owner role on the namespace to browse entities and messages.";

    public bool CanSave
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return false;
            }

            if (UseEntraId)
            {
                if (string.IsNullOrWhiteSpace(FullyQualifiedNamespace))
                {
                    return false;
                }

                return SelectedCredentialKind != EntraCredentialKind.ClientSecret ||
                       (!string.IsNullOrWhiteSpace(TenantId) &&
                        !string.IsNullOrWhiteSpace(ClientId) &&
                        !string.IsNullOrWhiteSpace(ClientSecret));
            }

            return ConnectionStringParser.LooksValid(ConnectionString);
        }
    }

    partial void OnSelectedCredentialKindChanged(EntraCredentialKind value) =>
        OnPropertyChanged(nameof(SelectedCredentialIndex));

    /// <summary>
    /// When the user pastes a connection string before naming the namespace, fill the
    /// name in from the endpoint host — that's almost always what they'd have typed.
    /// </summary>
    partial void OnConnectionStringChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(Name))
        {
            return;
        }

        var host = ConnectionStringParser.TryGetEndpointHost(value);
        if (!string.IsNullOrWhiteSpace(host))
        {
            Name = host.Split('.')[0];
        }
    }

    /// <summary>Builds the connection to save. Call only when <see cref="CanSave"/> is true.</summary>
    public NamespaceConnection ToConnection() => new()
    {
        Id = id,
        Name = Name.Trim(),
        Kind = UseEventHubs ? NamespaceKind.EventHubs : NamespaceKind.ServiceBus,
        AuthenticationMode = UseEntraId ? AuthenticationMode.EntraId : AuthenticationMode.ConnectionString,
        ConnectionString = UseEntraId ? null : ConnectionString.Trim(),
        FullyQualifiedNamespace = UseEntraId ? NormalizeNamespace(FullyQualifiedNamespace) : null,
        EntraCredentialKind = SelectedCredentialKind,
        TenantId = NullIfBlank(TenantId),
        ClientId = NullIfBlank(ClientId),
        ClientSecret = SelectedCredentialKind == EntraCredentialKind.ClientSecret ? NullIfBlank(ClientSecret) : null,
        Transport = UseWebSockets ? ServiceBusTransport.AmqpWebSockets : ServiceBusTransport.AmqpTcp,

        // The Event Hubs fields are cleared rather than kept when the kind is switched
        // back, so a saved Service Bus namespace never carries stale hub names.
        EventHubNames = UseEventHubs ? [.. ParseEventHubNames(EventHubNames)] : [],
        ConsumerGroup = UseEventHubs ? NullIfBlank(ConsumerGroup) : null,
        SubscriptionId = UseEventHubs ? NullIfBlank(SubscriptionId) : null
    };

    /// <summary>
    /// Splits the free-text hub list. People paste these from a portal blade or a script,
    /// so newlines, commas and semicolons all have to work, and duplicates are dropped.
    /// </summary>
    public static IReadOnlyList<string> ParseEventHubNames(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var names = new List<string>();
        foreach (var token in text.Split(['\n', '\r', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var name = token.Trim();
            if (name.Length > 0 && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// Accepts what people actually paste — a bare host, or a full <c>sb://host/</c> URL —
    /// and reduces it to the host the SDK expects.
    /// </summary>
    static string NormalizeNamespace(string value)
    {
        var trimmed = value.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.Host;
        }

        return trimmed.TrimEnd('/');
    }

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
