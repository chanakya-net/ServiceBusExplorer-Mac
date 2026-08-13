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
        useEntraId = connection.AuthenticationMode == AuthenticationMode.EntraId;
        connectionString = connection.ConnectionString ?? string.Empty;
        fullyQualifiedNamespace = connection.FullyQualifiedNamespace ?? string.Empty;
        tenantId = connection.TenantId ?? string.Empty;
        clientId = connection.ClientId ?? string.Empty;
        clientSecret = connection.ClientSecret ?? string.Empty;
        useWebSockets = connection.Transport == ServiceBusTransport.AmqpWebSockets;
        selectedCredentialKind = connection.EntraCredentialKind;
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
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ShowConnectionStringFields))]
    [NotifyPropertyChangedFor(nameof(ShowEntraFields))]
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
        AuthenticationMode = UseEntraId ? AuthenticationMode.EntraId : AuthenticationMode.ConnectionString,
        ConnectionString = UseEntraId ? null : ConnectionString.Trim(),
        FullyQualifiedNamespace = UseEntraId ? NormalizeNamespace(FullyQualifiedNamespace) : null,
        EntraCredentialKind = SelectedCredentialKind,
        TenantId = NullIfBlank(TenantId),
        ClientId = NullIfBlank(ClientId),
        ClientSecret = SelectedCredentialKind == EntraCredentialKind.ClientSecret ? NullIfBlank(ClientSecret) : null,
        Transport = UseWebSockets ? ServiceBusTransport.AmqpWebSockets : ServiceBusTransport.AmqpTcp
    };

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
