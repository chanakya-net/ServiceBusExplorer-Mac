namespace SbMac.Core.Connections;

/// <summary>
/// Which Entra ID credential flow to use. All of these work on macOS; the
/// interactive and device-code flows open a browser, the CLI flows reuse a
/// session you already established in a terminal.
/// </summary>
public enum EntraCredentialKind
{
    /// <summary>Azure.Identity's DefaultAzureCredential chain — tries environment, workload identity, managed identity, then the developer tools below.</summary>
    Default,

    /// <summary>Reuse the session from <c>az login</c>.</summary>
    AzureCli,

    /// <summary>Reuse the session from PowerShell's <c>Connect-AzAccount</c>.</summary>
    AzurePowerShell,

    /// <summary>Reuse the session from <c>azd auth login</c>.</summary>
    AzureDeveloperCli,

    /// <summary>Open the system browser and sign in interactively.</summary>
    InteractiveBrowser,

    /// <summary>Print a code to enter at microsoft.com/devicelogin — useful over SSH.</summary>
    DeviceCode,

    /// <summary>App registration with a client secret.</summary>
    ClientSecret
}
