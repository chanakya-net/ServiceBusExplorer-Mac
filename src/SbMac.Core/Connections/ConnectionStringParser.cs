namespace SbMac.Core.Connections;

/// <summary>
/// Minimal reader for Service Bus connection strings. We only need the endpoint
/// host (for display and keychain keying) and a validity check — the SDK does the
/// real parsing when it builds a client.
/// </summary>
public static class ConnectionStringParser
{
    /// <summary>
    /// Pulls the bare host out of the <c>Endpoint=</c> token, e.g.
    /// <c>Endpoint=sb://contoso.servicebus.windows.net/;...</c> → <c>contoso.servicebus.windows.net</c>.
    /// Returns null if the string is missing, malformed, or has no endpoint.
    /// </summary>
    public static string? TryGetEndpointHost(string? connectionString)
    {
        var endpoint = TryGetValue(connectionString, "Endpoint");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        // The endpoint uses the sb:// scheme, which Uri parses fine as a generic URI.
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    /// <summary>
    /// Reads a single <c>Key=Value</c> token. Values may themselves contain '=' (SAS
    /// keys are base64 and routinely end in padding), so we only split on the first one.
    /// </summary>
    public static string? TryGetValue(string? connectionString, string key)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        foreach (var token in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = token[..separator].Trim();
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return token[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// True when the string carries at least an endpoint and some form of credential.
    /// Deliberately permissive — we surface real errors from the SDK on first use rather
    /// than rejecting shapes we failed to anticipate.
    /// </summary>
    public static bool LooksValid(string? connectionString)
    {
        if (TryGetEndpointHost(connectionString) is null)
        {
            return false;
        }

        var hasSasKey = !string.IsNullOrWhiteSpace(TryGetValue(connectionString, "SharedAccessKey"));
        var hasSasSignature = !string.IsNullOrWhiteSpace(TryGetValue(connectionString, "SharedAccessSignature"));
        return hasSasKey || hasSasSignature;
    }

    /// <summary>
    /// Returns the connection string with its secret material replaced, for safe logging
    /// and for showing an existing entry in the UI without exposing the key.
    /// </summary>
    public static string Redact(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var redacted = parts.Select(token =>
        {
            var separator = token.IndexOf('=');
            if (separator <= 0)
            {
                return token;
            }

            var name = token[..separator].Trim();
            return name.Equals("SharedAccessKey", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("SharedAccessSignature", StringComparison.OrdinalIgnoreCase)
                ? $"{name}=****"
                : token;
        });

        return string.Join(";", redacted);
    }
}
