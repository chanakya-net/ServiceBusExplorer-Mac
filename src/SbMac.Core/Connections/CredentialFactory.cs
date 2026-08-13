using Azure.Core;
using Azure.Identity;

namespace SbMac.Core.Connections;

/// <summary>
/// Turns a <see cref="NamespaceConnection"/>'s Entra settings into an
/// <see cref="TokenCredential"/>. Every flow here works on macOS.
/// </summary>
public static class CredentialFactory
{
    /// <summary>
    /// Builds the credential described by <paramref name="connection"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The connection asks for a flow whose required fields (client id, secret, tenant) are missing.
    /// </exception>
    public static TokenCredential Create(NamespaceConnection connection)
    {
        var tenantId = string.IsNullOrWhiteSpace(connection.TenantId) ? null : connection.TenantId.Trim();

        return connection.EntraCredentialKind switch
        {
            EntraCredentialKind.AzureCli =>
                new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }),

            EntraCredentialKind.AzurePowerShell =>
                new AzurePowerShellCredential(new AzurePowerShellCredentialOptions { TenantId = tenantId }),

            EntraCredentialKind.AzureDeveloperCli =>
                new AzureDeveloperCliCredential(new AzureDeveloperCliCredentialOptions { TenantId = tenantId }),

            EntraCredentialKind.InteractiveBrowser =>
                new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
                {
                    TenantId = tenantId,
                    ClientId = NullIfBlank(connection.ClientId),
                    // Cache the token so the browser doesn't reopen on every app start.
                    TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "SB-Mac" }
                }),

            EntraCredentialKind.DeviceCode =>
                new DeviceCodeCredential(new DeviceCodeCredentialOptions
                {
                    TenantId = tenantId,
                    ClientId = NullIfBlank(connection.ClientId),
                    TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "SB-Mac" }
                }),

            EntraCredentialKind.ClientSecret => CreateClientSecretCredential(connection, tenantId),

            EntraCredentialKind.Default or _ =>
                new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    TenantId = tenantId,
                    // The browser fallback is opt-in in DefaultAzureCredential; enabling it means
                    // a developer with no CLI session still gets a way to sign in.
                    ExcludeInteractiveBrowserCredential = false
                })
        };
    }

    static TokenCredential CreateClientSecretCredential(NamespaceConnection connection, string? tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new InvalidOperationException("A tenant ID is required for client secret authentication.");
        }

        if (string.IsNullOrWhiteSpace(connection.ClientId))
        {
            throw new InvalidOperationException("A client ID is required for client secret authentication.");
        }

        if (string.IsNullOrWhiteSpace(connection.ClientSecret))
        {
            throw new InvalidOperationException("A client secret is required for client secret authentication.");
        }

        return new ClientSecretCredential(tenantId, connection.ClientId.Trim(), connection.ClientSecret);
    }

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Human-readable name for a credential kind, for the connection dialog.
    /// </summary>
    public static string Describe(EntraCredentialKind kind) => kind switch
    {
        EntraCredentialKind.Default => "Default (environment, managed identity, then developer tools)",
        EntraCredentialKind.AzureCli => "Azure CLI (az login)",
        EntraCredentialKind.AzurePowerShell => "Azure PowerShell (Connect-AzAccount)",
        EntraCredentialKind.AzureDeveloperCli => "Azure Developer CLI (azd auth login)",
        EntraCredentialKind.InteractiveBrowser => "Interactive browser sign-in",
        EntraCredentialKind.DeviceCode => "Device code",
        EntraCredentialKind.ClientSecret => "App registration (client secret)",
        _ => kind.ToString()
    };
}
