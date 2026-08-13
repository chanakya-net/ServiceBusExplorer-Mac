namespace SbMac.Core.Connections;

/// <summary>
/// How SB-Mac authenticates against a Service Bus namespace.
/// </summary>
public enum AuthenticationMode
{
    /// <summary>SAS connection string, as issued by the Azure portal.</summary>
    ConnectionString,

    /// <summary>Entra ID (formerly Azure AD) token credential — no secret stored on disk.</summary>
    EntraId
}
