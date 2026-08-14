using System.Net.Http.Json;
using System.Text.Json.Serialization;

using SbMac.Core;

namespace SbMac.App.Services;

public sealed class UpdateChecker(
    HttpClient httpClient,
    IAppVersionProvider versionProvider,
    IUpdateCheckStateStore stateStore,
    TimeProvider timeProvider,
    TimeSpan requestTimeout) : IUpdateChecker
{
    static readonly Uri LatestReleaseEndpoint = new(
        "https://api.github.com/repos/chanakya-net/ServiceBusExplorer-Mac/releases/latest");

    static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    internal static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(5);

    readonly SemaphoreSlim checkGate = new(1, 1);
    DateTimeOffset? lastAttemptUtc;

    public static IUpdateChecker? CreateDefault()
    {
        try
        {
            return new UpdateChecker(
                new HttpClient(),
                new BundleVersionProvider(),
                new JsonUpdateCheckStateStore(AppPaths.SettingsFile),
                TimeProvider.System,
                DefaultRequestTimeout);
        }
        catch
        {
            // Even constructing the ancillary service must not prevent startup.
            return null;
        }
    }

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        Version? installed;
        try
        {
            installed = versionProvider.GetCurrentVersion();
        }
        catch
        {
            return null;
        }

        if (installed is null || installed == new Version(0, 0, 0))
        {
            return null;
        }

        try
        {
            await checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            if (lastAttemptUtc is { } inMemoryPrevious && now - inMemoryPrevious < CheckInterval)
            {
                return null;
            }

            DateTimeOffset? lastCheck = null;
            try
            {
                lastCheck = await stateStore.GetLastCheckUtcAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // State is best-effort; a read failure behaves like no previous check.
            }

            if (lastCheck is { } previous && now - previous < CheckInterval)
            {
                return null;
            }

            lastAttemptUtc = now;
            try
            {
                await stateStore.SetLastCheckUtcAsync(now, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The current in-memory check can continue even when persistence fails.
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
                request.Headers.UserAgent.ParseAdd("SB-Mac-UpdateChecker/1.0");

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(requestTimeout);

                using var response = await httpClient.SendAsync(request, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var release = await response.Content
                    .ReadFromJsonAsync<GitHubRelease>(cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
                var available = ReleaseVersion.Parse(release?.TagName, allowLeadingV: true);

                if (available is null || available <= installed ||
                    !Uri.TryCreate(release?.HtmlUrl, UriKind.Absolute, out var releaseUri) ||
                    releaseUri.Scheme != Uri.UriSchemeHttps ||
                    !releaseUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                return new UpdateInfo(installed, available, releaseUri);
            }
            catch
            {
                return null;
            }
        }
        finally
        {
            checkGate.Release();
        }
    }

    sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
