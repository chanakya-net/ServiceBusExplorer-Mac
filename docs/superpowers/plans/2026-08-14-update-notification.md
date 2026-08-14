# Update Notification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Notify users once per day when a newer stable SB-Mac release is available, with actions to defer or open the release page.

**Architecture:** A testable update service reads the installed bundle version, enforces a persisted 24-hour cadence, and compares it with GitHub's latest stable release. The production composition root injects the checker into the main view model; the existing UI boundary owns the modal prompt and external-browser launch.

**Tech Stack:** .NET 10, C# 14, Avalonia 12.1, `HttpClient`, `System.Text.Json`, xUnit v3, Avalonia Headless XUnit

**Spec:** `docs/superpowers/specs/2026-08-14-update-notification-design.md`

## Global Constraints

- Notification only: do not download, install, mount, or relaunch an update.
- Query only `https://api.github.com/repos/chanakya-net/ServiceBusExplorer-Mac/releases/latest`.
- Notify only for a stable three-component version strictly newer than the installed version.
- Treat `CFBundleShortVersionString` as the canonical installed version.
- Skip unbundled, malformed, and `0.0.0` builds.
- Attempt at most once per 24 hours and record the attempt before the network request.
- Preserve unknown properties in `settings.json`.
- Use a five-second production request timeout.
- Open only absolute HTTPS release URLs hosted on `github.com`.
- Keep update, settings, network, and browser failures silent and non-blocking.
- Add no new NuGet or native-framework dependency.

---

## File Structure

### New application files

- `src/SbMac.App/Services/IUpdateChecker.cs` — public update result and checker/version-provider contracts.
- `src/SbMac.App/Services/ReleaseVersion.cs` — strict three-component version parsing.
- `src/SbMac.App/Services/BundleVersionProvider.cs` — reads the packaged app's `Info.plist`.
- `src/SbMac.App/Services/UpdateCheckStateStore.cs` — reads and updates only `lastUpdateCheckUtc` in `settings.json`.
- `src/SbMac.App/Services/UpdateChecker.cs` — cadence enforcement, GitHub request, validation, and comparison.
- `src/SbMac.App/Services/IUriLauncher.cs` — platform browser-launch boundary and production implementation.

### New test files

- `tests/SbMac.Tests/BundleVersionProviderTests.cs`
- `tests/SbMac.Tests/UpdateCheckStateStoreTests.cs`
- `tests/SbMac.Tests/UpdateCheckerTests.cs`
- `tests/SbMac.Tests/UpdateNotificationUiTests.cs`

### Modified files

- `src/SbMac.App/App.axaml.cs` — create and inject the production checker.
- `src/SbMac.App/Services/IUiServices.cs` — expose the update-notification operation.
- `src/SbMac.App/ViewModels/MainWindowViewModel.cs` — invoke the optional checker after existing initialization.
- `src/SbMac.App/Views/MainWindow.axaml.cs` — display the notification and launch the validated URL.
- `src/SbMac.App/Views/Dialogs/MessageDialog.cs` — support a custom dismiss label.
- `README.md` — document the once-daily notification behavior.

---

### Task 1: Installed Bundle Version

**Files:**
- Create: `src/SbMac.App/Services/IUpdateChecker.cs`
- Create: `src/SbMac.App/Services/ReleaseVersion.cs`
- Create: `src/SbMac.App/Services/BundleVersionProvider.cs`
- Create: `tests/SbMac.Tests/BundleVersionProviderTests.cs`

**Interfaces:**
- Consumes: `AppContext.BaseDirectory` and the bundle key `CFBundleShortVersionString`.
- Produces: `UpdateInfo`, `IUpdateChecker.CheckAsync(CancellationToken)`, `IAppVersionProvider.GetCurrentVersion()`, and `ReleaseVersion.Parse(string?)`.

- [ ] **Step 1: Write failing bundle-version tests**

Create `tests/SbMac.Tests/BundleVersionProviderTests.cs` with real temporary bundle layouts:

```csharp
using SbMac.App.Services;

using Xunit;

namespace SbMac.Tests;

public sealed class BundleVersionProviderTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("0.0.0", 0, 0, 0)]
    public void ReadsThreePartVersion(string text, int major, int minor, int patch)
    {
        using var bundle = TestBundle.Create(text);

        var version = new BundleVersionProvider(bundle.ExecutableDirectory).GetCurrentVersion();

        Assert.Equal(new Version(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("one.two.three")]
    public void RejectsInvalidBundleVersion(string text)
    {
        using var bundle = TestBundle.Create(text);

        Assert.Null(new BundleVersionProvider(bundle.ExecutableDirectory).GetCurrentVersion());
    }

    [Fact]
    public void MissingBundleMetadataReturnsNull()
    {
        using var bundle = TestBundle.CreateWithoutInfoPlist();

        Assert.Null(new BundleVersionProvider(bundle.ExecutableDirectory).GetCurrentVersion());
    }

    sealed class TestBundle : IDisposable
    {
        TestBundle(string root)
        {
            Root = root;
            ExecutableDirectory = Path.Combine(root, "SB-Mac.app", "Contents", "MacOS");
            Directory.CreateDirectory(ExecutableDirectory);
        }

        public string Root { get; }
        public string ExecutableDirectory { get; }

        public static TestBundle Create(string version)
        {
            var bundle = CreateWithoutInfoPlist();
            File.WriteAllText(
                Path.Combine(bundle.ExecutableDirectory, "..", "Info.plist"),
                $"""<plist><dict><key>CFBundleShortVersionString</key><string>{version}</string></dict></plist>""");
            return bundle;
        }

        public static TestBundle CreateWithoutInfoPlist() =>
            new(Path.Combine(Path.GetTempPath(), $"sbmac-bundle-{Guid.NewGuid():N}"));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests and confirm the missing types fail compilation**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter FullyQualifiedName~BundleVersionProviderTests
```

Expected: FAIL because `BundleVersionProvider` and `SbMac.App.Services` update contracts do not exist.

- [ ] **Step 3: Add the update contracts**

Create `src/SbMac.App/Services/IUpdateChecker.cs`:

```csharp
namespace SbMac.App.Services;

public sealed record UpdateInfo(
    Version InstalledVersion,
    Version AvailableVersion,
    Uri ReleaseUri);

public interface IUpdateChecker
{
    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default);
}

public interface IAppVersionProvider
{
    Version? GetCurrentVersion();
}
```

- [ ] **Step 4: Add strict version parsing**

Create `src/SbMac.App/Services/ReleaseVersion.cs`:

```csharp
namespace SbMac.App.Services;

static class ReleaseVersion
{
    public static Version? Parse(string? text, bool allowLeadingV = false)
    {
        var value = text?.Trim();
        if (allowLeadingV && value is { Length: > 1 } && value[0] == 'v')
        {
            value = value[1..];
        }

        var parts = value?.Split('.');
        if (parts is not { Length: 3 } ||
            !int.TryParse(parts[0], out var major) ||
            !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch) ||
            major < 0 || minor < 0 || patch < 0)
        {
            return null;
        }

        return new Version(major, minor, patch);
    }
}
```

- [ ] **Step 5: Read the canonical version from `Info.plist`**

Create `src/SbMac.App/Services/BundleVersionProvider.cs`:

```csharp
using System.Xml.Linq;

namespace SbMac.App.Services;

public sealed class BundleVersionProvider(string? executableDirectory = null) : IAppVersionProvider
{
    readonly string executableDirectory = executableDirectory ?? AppContext.BaseDirectory;

    public Version? GetCurrentVersion()
    {
        try
        {
            var infoPlist = Path.GetFullPath(Path.Combine(executableDirectory, "..", "Info.plist"));
            var elements = XDocument.Load(infoPlist).Root?.Element("dict")?.Elements().ToList();
            if (elements is null)
            {
                return null;
            }

            for (var index = 0; index + 1 < elements.Count; index++)
            {
                if (elements[index].Name == "key" &&
                    elements[index].Value == "CFBundleShortVersionString" &&
                    elements[index + 1].Name == "string")
                {
                    return ReleaseVersion.Parse(elements[index + 1].Value);
                }
            }
        }
        catch
        {
            // An unbundled or unreadable build is not eligible for update checks.
        }

        return null;
    }
}
```

- [ ] **Step 6: Run the focused tests**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter FullyQualifiedName~BundleVersionProviderTests
```

Expected: PASS for all valid, malformed, and missing-bundle cases.

- [ ] **Step 7: Commit the installed-version unit**

```bash
git add src/SbMac.App/Services/IUpdateChecker.cs \
  src/SbMac.App/Services/ReleaseVersion.cs \
  src/SbMac.App/Services/BundleVersionProvider.cs \
  tests/SbMac.Tests/BundleVersionProviderTests.cs
git commit -m "feat(update): read installed bundle version"
```

---

### Task 2: Persisted 24-Hour Cadence

**Files:**
- Create: `src/SbMac.App/Services/UpdateCheckStateStore.cs`
- Create: `tests/SbMac.Tests/UpdateCheckStateStoreTests.cs`

**Interfaces:**
- Consumes: a caller-supplied `settings.json` path and UTC timestamps.
- Produces: `IUpdateCheckStateStore.GetLastCheckUtcAsync(CancellationToken)` and `SetLastCheckUtcAsync(DateTimeOffset, CancellationToken)`.

- [ ] **Step 1: Write failing settings-store tests**

Create `tests/SbMac.Tests/UpdateCheckStateStoreTests.cs`:

```csharp
using System.Text.Json.Nodes;

using SbMac.App.Services;

using Xunit;

namespace SbMac.Tests;

public sealed class UpdateCheckStateStoreTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), $"sbmac-settings-{Guid.NewGuid():N}");

    string SettingsFile => Path.Combine(directory, "settings.json");

    [Fact]
    public async Task MissingSettingsBehaveLikeNoPreviousCheck()
    {
        Assert.Null(await new JsonUpdateCheckStateStore(SettingsFile).GetLastCheckUtcAsync());
    }

    [Fact]
    public async Task TimestampRoundTripsAsUtc()
    {
        var store = new JsonUpdateCheckStateStore(SettingsFile);
        var timestamp = new DateTimeOffset(2026, 8, 14, 4, 30, 0, TimeSpan.Zero);

        await store.SetLastCheckUtcAsync(timestamp);

        Assert.Equal(timestamp, await store.GetLastCheckUtcAsync());
    }

    [Fact]
    public async Task UpdatingTimestampPreservesUnknownSettings()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(SettingsFile, """{"theme":"system","futureFlag":true}""");

        await new JsonUpdateCheckStateStore(SettingsFile)
            .SetLastCheckUtcAsync(new DateTimeOffset(2026, 8, 14, 4, 30, 0, TimeSpan.Zero));

        var root = Assert.IsType<JsonObject>(JsonNode.Parse(await File.ReadAllTextAsync(SettingsFile)));
        Assert.Equal("system", root["theme"]!.GetValue<string>());
        Assert.True(root["futureFlag"]!.GetValue<bool>());
        Assert.NotNull(root["lastUpdateCheckUtc"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"lastUpdateCheckUtc\":\"not-a-date\"}")]
    public async Task InvalidStateBehavesLikeNoPreviousCheck(string json)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(SettingsFile, json);

        Assert.Null(await new JsonUpdateCheckStateStore(SettingsFile).GetLastCheckUtcAsync());
    }

    [Fact]
    public async Task SavingReplacesInvalidJsonWithValidState()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(SettingsFile, "not json");
        var timestamp = new DateTimeOffset(2026, 8, 14, 4, 30, 0, TimeSpan.Zero);

        var store = new JsonUpdateCheckStateStore(SettingsFile);
        await store.SetLastCheckUtcAsync(timestamp);

        Assert.Equal(timestamp, await store.GetLastCheckUtcAsync());
    }

    [Fact]
    public async Task WriteFailureDoesNotEscape()
    {
        Directory.CreateDirectory(directory);
        var fileWhereDirectoryIsNeeded = Path.Combine(directory, "blocked");
        await File.WriteAllTextAsync(fileWhereDirectoryIsNeeded, "content");

        var store = new JsonUpdateCheckStateStore(Path.Combine(fileWhereDirectoryIsNeeded, "settings.json"));

        await store.SetLastCheckUtcAsync(DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm the store is missing**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter FullyQualifiedName~UpdateCheckStateStoreTests
```

Expected: FAIL because `JsonUpdateCheckStateStore` does not exist.

- [ ] **Step 3: Implement the JSON state store**

Create `src/SbMac.App/Services/UpdateCheckStateStore.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SbMac.App.Services;

public interface IUpdateCheckStateStore
{
    Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default);
    Task SetLastCheckUtcAsync(DateTimeOffset value, CancellationToken cancellationToken = default);
}

public sealed class JsonUpdateCheckStateStore(string filePath) : IUpdateCheckStateStore
{
    public async Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var root = JsonNode.Parse(await File.ReadAllTextAsync(filePath, cancellationToken)) as JsonObject;
            var text = root?["lastUpdateCheckUtc"]?.GetValue<string>();

            return DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var value)
                ? value
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task SetLastCheckUtcAsync(
        DateTimeOffset value,
        CancellationToken cancellationToken = default)
    {
        try
        {
            JsonObject root = new();
            if (File.Exists(filePath))
            {
                try
                {
                    root = JsonNode.Parse(await File.ReadAllTextAsync(filePath, cancellationToken)) as JsonObject
                        ?? new JsonObject();
                }
                catch (JsonException)
                {
                    // Invalid settings contain no preservable properties; replace them.
                }
            }

            root["lastUpdateCheckUtc"] = value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            await File.WriteAllTextAsync(
                filePath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            // Update state is best-effort and must not affect application startup.
        }
    }
}
```

- [ ] **Step 4: Run the focused tests**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter FullyQualifiedName~UpdateCheckStateStoreTests
```

Expected: PASS, including unknown-property preservation and ignored write failure.

- [ ] **Step 5: Commit the cadence store**

```bash
git add src/SbMac.App/Services/UpdateCheckStateStore.cs \
  tests/SbMac.Tests/UpdateCheckStateStoreTests.cs
git commit -m "feat(update): persist daily check cadence"
```

---

### Task 3: GitHub Latest-Release Checker

**Files:**
- Create: `src/SbMac.App/Services/UpdateChecker.cs`
- Create: `tests/SbMac.Tests/UpdateCheckerTests.cs`

**Interfaces:**
- Consumes: `IAppVersionProvider`, `IUpdateCheckStateStore`, `HttpClient`, and `TimeProvider`.
- Produces: `UpdateChecker.CheckAsync(CancellationToken)` and `UpdateChecker.CreateDefault()`.

- [ ] **Step 1: Write failing checker tests and deterministic fakes**

Create `tests/SbMac.Tests/UpdateCheckerTests.cs` with these fixtures and core cases:

```csharp
using System.Net;
using System.Text;

using SbMac.App.Services;

using Xunit;

namespace SbMac.Tests;

public sealed class UpdateCheckerTests
{
    static readonly DateTimeOffset Now = new(2026, 8, 14, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task NewerReleaseIsReturnedAndAttemptIsRecordedFirst()
    {
        var events = new List<string>();
        var state = new RecordingStateStore(events);
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            """{"tag_name":"v1.4.0","html_url":"https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/v1.4.0"}""")), events);
        var checker = Build(new Version(1, 3, 2), state, handler);

        var update = await checker.CheckAsync();

        Assert.Equal(new Version(1, 3, 2), update!.InstalledVersion);
        Assert.Equal(new Version(1, 4, 0), update.AvailableVersion);
        Assert.Equal("github.com", update.ReleaseUri.Host);
        Assert.Equal(Now, state.LastCheckUtc);
        Assert.Equal(["state", "request"], events);
        Assert.Contains("SB-Mac", handler.Request!.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData("v1.3.2")]
    [InlineData("v1.2.9")]
    [InlineData("1.4")]
    [InlineData("preview")]
    public async Task EqualOlderOrMalformedReleaseDoesNotNotify(string tag)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            $$"""{"tag_name":"{{tag}}","html_url":"https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/{{tag}}"}""")));

        Assert.Null(await Build(new Version(1, 3, 2), new RecordingStateStore(), handler).CheckAsync());
    }

    [Fact]
    public async Task CheckInside24HoursMakesNoRequest()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("must not request"));
        var state = new RecordingStateStore { LastCheckUtc = Now.AddHours(-23).AddMinutes(-59) };

        Assert.Null(await Build(new Version(1, 3, 2), state, handler).CheckAsync());
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task CheckAt24HourBoundaryMakesRequest()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        var state = new RecordingStateStore { LastCheckUtc = Now.AddHours(-24) };

        await Build(new Version(1, 3, 2), state, handler).CheckAsync();

        Assert.NotNull(handler.Request);
        Assert.Equal(Now, state.LastCheckUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0.0.0")]
    public async Task UnbundledOrUnversionedBuildSkipsEverything(string? version)
    {
        var parsed = version is null ? null : Version.Parse(version);
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("must not request"));

        Assert.Null(await Build(parsed, new RecordingStateStore(), handler).CheckAsync());
        Assert.Null(handler.Request);
    }

    [Fact]
    public async Task BundleVersionFailureIsSilent()
    {
        var handler = new RecordingHandler((_, _) => throw new InvalidOperationException("must not request"));
        var checker = new UpdateChecker(
            new HttpClient(handler),
            new ThrowingVersionProvider(),
            new RecordingStateStore(),
            new StubTimeProvider(Now),
            TimeSpan.FromSeconds(1));

        Assert.Null(await checker.CheckAsync());
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpFailureIsSilentAndStillRecordsAttempt(HttpStatusCode status)
    {
        var events = new List<string>();
        var state = new RecordingStateStore(events);
        var handler = new RecordingHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(status)), events);

        Assert.Null(await Build(new Version(1, 3, 2), state, handler).CheckAsync());
        Assert.Equal(Now, state.LastCheckUtc);
        Assert.Equal(["state", "request"], events);
    }

    [Fact]
    public async Task StateStoreExceptionsDoNotBlockTheNetworkCheck()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            """{"tag_name":"v1.4.0","html_url":"https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/v1.4.0"}""")));

        var update = await Build(new Version(1, 3, 2), new ThrowingStateStore(), handler).CheckAsync();

        Assert.Equal(new Version(1, 4, 0), update!.AvailableVersion);
    }

    [Fact]
    public async Task TimeoutIsSilent()
    {
        var handler = new RecordingHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });

        Assert.Null(await Build(
            new Version(1, 3, 2), new RecordingStateStore(), handler, TimeSpan.FromMilliseconds(10)).CheckAsync());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"tag_name\":\"v1.4.0\"}")]
    [InlineData("{\"tag_name\":\"v1.4.0\",\"html_url\":\"http://github.com/release\"}")]
    [InlineData("{\"tag_name\":\"v1.4.0\",\"html_url\":\"https://example.com/release\"}")]
    public async Task InvalidResponseIsSilent(string json)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(json)));

        Assert.Null(await Build(new Version(1, 3, 2), new RecordingStateStore(), handler).CheckAsync());
    }

    static UpdateChecker Build(
        Version? installed,
        IUpdateCheckStateStore state,
        RecordingHandler handler,
        TimeSpan? timeout = null) =>
        new(
            new HttpClient(handler),
            new StubVersionProvider(installed),
            state,
            new StubTimeProvider(Now),
            timeout ?? TimeSpan.FromSeconds(1));

    static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    sealed class StubVersionProvider(Version? version) : IAppVersionProvider
    {
        public Version? GetCurrentVersion() => version;
    }

    sealed class ThrowingVersionProvider : IAppVersionProvider
    {
        public Version? GetCurrentVersion() => throw new IOException("bundle unreadable");
    }

    sealed class StubTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    sealed class RecordingStateStore(List<string>? events = null) : IUpdateCheckStateStore
    {
        public DateTimeOffset? LastCheckUtc { get; set; }

        public Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LastCheckUtc);

        public Task SetLastCheckUtcAsync(DateTimeOffset value, CancellationToken cancellationToken = default)
        {
            LastCheckUtc = value;
            events?.Add("state");
            return Task.CompletedTask;
        }
    }

    sealed class ThrowingStateStore : IUpdateCheckStateStore
    {
        public Task<DateTimeOffset?> GetLastCheckUtcAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("read failed");

        public Task SetLastCheckUtcAsync(
            DateTimeOffset value,
            CancellationToken cancellationToken = default) =>
            throw new IOException("write failed");
    }

    sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        List<string>? events = null) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            events?.Add("request");
            return send(request, cancellationToken);
        }
    }
}
```

- [ ] **Step 2: Run the tests and confirm `UpdateChecker` is missing**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter FullyQualifiedName~UpdateCheckerTests
```

Expected: FAIL because `UpdateChecker` does not exist.

- [ ] **Step 3: Implement the checker**

Create `src/SbMac.App/Services/UpdateChecker.cs`:

```csharp
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

    public static IUpdateChecker? CreateDefault()
    {
        try
        {
            return new UpdateChecker(
                new HttpClient(),
                new BundleVersionProvider(),
                new JsonUpdateCheckStateStore(AppPaths.SettingsFile),
                TimeProvider.System,
                TimeSpan.FromSeconds(5));
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

        var now = timeProvider.GetUtcNow();
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

    sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);
}
```

- [ ] **Step 4: Run the focused checker tests**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter FullyQualifiedName~UpdateCheckerTests
```

Expected: PASS for newer/equal/older versions, cadence, malformed data, server errors, and timeout.

- [ ] **Step 5: Run all non-UI tests to catch contract regressions**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter "FullyQualifiedName!~UiSmokeTests&FullyQualifiedName!~UpdateNotificationUiTests"
```

Expected: PASS.

- [ ] **Step 6: Commit the release checker**

```bash
git add src/SbMac.App/Services/UpdateChecker.cs \
  tests/SbMac.Tests/UpdateCheckerTests.cs
git commit -m "feat(update): check latest GitHub release"
```

---

### Task 4: Startup Notification and Browser Action

**Files:**
- Create: `src/SbMac.App/Services/IUriLauncher.cs`
- Create: `tests/SbMac.Tests/UpdateNotificationUiTests.cs`
- Modify: `src/SbMac.App/App.axaml.cs:14-27`
- Modify: `src/SbMac.App/Services/IUiServices.cs:25-67`
- Modify: `src/SbMac.App/ViewModels/MainWindowViewModel.cs:26-64,341-381`
- Modify: `src/SbMac.App/Views/MainWindow.axaml.cs:18-78`
- Modify: `src/SbMac.App/Views/Dialogs/MessageDialog.cs:19-105`

**Interfaces:**
- Consumes: `IUpdateChecker.CheckAsync`, `UpdateInfo`, and an `IUriLauncher` implementation.
- Produces: `IUiServices.ShowUpdateAvailableAsync(UpdateInfo)` and a notification with `Later` and `View Release` actions.

- [ ] **Step 1: Write failing dialog and browser-action tests**

Create `tests/SbMac.Tests/UpdateNotificationUiTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;

using SbMac.App.Services;
using SbMac.App.Views;
using SbMac.App.Views.Dialogs;

using Xunit;

namespace SbMac.Tests;

public sealed class UpdateNotificationUiTests
{
    static readonly UpdateInfo Update = new(
        new Version(1, 3, 2),
        new Version(1, 4, 0),
        new Uri("https://github.com/chanakya-net/ServiceBusExplorer-Mac/releases/tag/v1.4.0"));

    [AvaloniaFact]
    public async Task LaterClosesPromptWithoutOpeningBrowser()
    {
        var launcher = new RecordingUriLauncher();
        var owner = new MainWindow(launcher);
        owner.Show();

        var prompt = owner.ShowUpdateAvailableAsync(Update);
        var dialog = Assert.IsType<MessageDialog>(Assert.Single(owner.OwnedWindows));
        var buttons = GetButtons(dialog);

        Assert.Equal("Later", buttons[0].Content);
        Assert.Equal("View Release", buttons[1].Content);
        Assert.Contains("Version 1.4.0 is available", GetMessage(dialog));
        Assert.Contains("using 1.3.2", GetMessage(dialog));
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await prompt;
        Assert.Null(launcher.OpenedUri);
    }

    [AvaloniaFact]
    public async Task ViewReleaseOpensValidatedReleaseUri()
    {
        var launcher = new RecordingUriLauncher();
        var owner = new MainWindow(launcher);
        owner.Show();

        var prompt = owner.ShowUpdateAvailableAsync(Update);
        var dialog = Assert.IsType<MessageDialog>(Assert.Single(owner.OwnedWindows));
        var buttons = GetButtons(dialog);

        buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await prompt;
        Assert.Equal(Update.ReleaseUri, launcher.OpenedUri);
    }

    [AvaloniaFact]
    public async Task BrowserLauncherFailureIsSilent()
    {
        var owner = new MainWindow(new ThrowingUriLauncher());
        owner.Show();

        var prompt = owner.ShowUpdateAvailableAsync(Update);
        var dialog = Assert.IsType<MessageDialog>(Assert.Single(owner.OwnedWindows));
        GetButtons(dialog)[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await prompt;
    }

    static IReadOnlyList<Button> GetButtons(MessageDialog dialog)
    {
        var root = Assert.IsType<StackPanel>(dialog.Content);
        return Assert.IsType<StackPanel>(root.Children[2]).Children.Cast<Button>().ToList();
    }

    static string GetMessage(MessageDialog dialog)
    {
        var root = Assert.IsType<StackPanel>(dialog.Content);
        var scroller = Assert.IsType<ScrollViewer>(root.Children[1]);
        return Assert.IsType<TextBlock>(scroller.Content).Text!;
    }

    sealed class RecordingUriLauncher : IUriLauncher
    {
        public Uri? OpenedUri { get; private set; }

        public Task OpenAsync(Uri uri)
        {
            OpenedUri = uri;
            return Task.CompletedTask;
        }
    }

    sealed class ThrowingUriLauncher : IUriLauncher
    {
        public Task OpenAsync(Uri uri) => throw new InvalidOperationException("browser unavailable");
    }
}
```

- [ ] **Step 2: Run the tests and confirm the UI contracts are missing**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter FullyQualifiedName~UpdateNotificationUiTests
```

Expected: FAIL because `IUriLauncher`, the injected `MainWindow` constructor, and `ShowUpdateAvailableAsync` do not exist.

- [ ] **Step 3: Add the browser-launch boundary**

Create `src/SbMac.App/Services/IUriLauncher.cs`:

```csharp
using System.Diagnostics;

namespace SbMac.App.Services;

public interface IUriLauncher
{
    Task OpenAsync(Uri uri);
}

public sealed class SystemUriLauncher : IUriLauncher
{
    public Task OpenAsync(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Opening release notes is best-effort and must not interrupt the app.
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Let confirmation dialogs name their dismiss action**

Change the `MessageDialog` constructor to accept `string dismissText`, set the
cancel button content from it, and add an optional argument to `ConfirmAsync`:

```csharp
MessageDialog(
    string title,
    string message,
    MessageDialogKind kind,
    string? confirmText,
    bool destructive,
    string dismissText)
```

```csharp
var cancel = new Button { Content = dismissText, MinWidth = 88, IsCancel = true };
```

```csharp
public static async Task<bool> ConfirmAsync(
    Window owner,
    string title,
    string message,
    string confirmText,
    bool destructive,
    string dismissText = "Cancel")
{
    var dialog = new MessageDialog(
        title, message, MessageDialogKind.Info, confirmText, destructive, dismissText);
    return await dialog.ShowDialog<bool>(owner);
}
```

Pass `"OK"` from `ShowAsync` when constructing an informational dialog so all
constructor calls remain explicit.

- [ ] **Step 5: Add the UI service operation and implementation**

Add to `IUiServices`:

```csharp
Task ShowUpdateAvailableAsync(UpdateInfo update);
```

Add the required `using SbMac.App.Services;` to the interface file. Change
`MainWindow` construction and implement the operation:

```csharp
readonly IUriLauncher uriLauncher;

public MainWindow(IUriLauncher? uriLauncher = null)
{
    this.uriLauncher = uriLauncher ?? new SystemUriLauncher();
    InitializeComponent();
    MessageGrid.SelectionChanged += OnMessageSelectionChanged;
}
```

```csharp
public async Task ShowUpdateAvailableAsync(UpdateInfo update)
{
    var viewRelease = await MessageDialog.ConfirmAsync(
        this,
        "Update available",
        $"Version {update.AvailableVersion} is available. You are using {update.InstalledVersion}.",
        "View Release",
        destructive: false,
        dismissText: "Later");

    if (viewRelease)
    {
        try
        {
            await uriLauncher.OpenAsync(update.ReleaseUri);
        }
        catch
        {
            // Opening the release page is best-effort.
        }
    }
}
```

- [ ] **Step 6: Inject and invoke the optional checker**

Add a nullable checker field to `MainWindowViewModel`, preserve its parameterless
usage through an optional constructor argument, and call the isolated helper after
the existing namespace-load `try`/`catch`:

```csharp
readonly IUpdateChecker? updateChecker;

public MainWindowViewModel(IUpdateChecker? updateChecker = null)
{
    this.updateChecker = updateChecker;
    Namespaces = [];
    Messages = [];
    Log = [];
    Operations = [];

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
```

```csharp
await NotifyAboutUpdatesAsync().ConfigureAwait(true);
```

```csharp
async Task NotifyAboutUpdatesAsync()
{
    if (updateChecker is null || Ui is null)
    {
        return;
    }

    try
    {
        if (await updateChecker.CheckAsync().ConfigureAwait(true) is { } update)
        {
            await Ui.ShowUpdateAvailableAsync(update).ConfigureAwait(true);
        }
    }
    catch
    {
        // Update notification is ancillary and cannot fail application startup.
    }
}
```

In `App.OnFrameworkInitializationCompleted`, change the production construction
to:

```csharp
var viewModel = new MainWindowViewModel(UpdateChecker.CreateDefault());
```

Add `using SbMac.App.Services;` to `App.axaml.cs` and
`MainWindowViewModel.cs`. Parameterless construction in previews and existing
tests remains network-free.

- [ ] **Step 7: Run the focused UI tests**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter FullyQualifiedName~UpdateNotificationUiTests
```

Expected: PASS; `Later` does not launch and `View Release` passes the exact validated URI to the launcher.

- [ ] **Step 8: Run all UI smoke tests**

Run:

```bash
dotnet test tests/SbMac.Tests/SbMac.Tests.csproj --filter "FullyQualifiedName~UiSmokeTests|FullyQualifiedName~UpdateNotificationUiTests"
```

Expected: PASS and no test makes a real GitHub request.

- [ ] **Step 9: Commit startup and UI integration**

```bash
git add src/SbMac.App/App.axaml.cs \
  src/SbMac.App/Services/IUiServices.cs \
  src/SbMac.App/Services/IUriLauncher.cs \
  src/SbMac.App/ViewModels/MainWindowViewModel.cs \
  src/SbMac.App/Views/MainWindow.axaml.cs \
  src/SbMac.App/Views/Dialogs/MessageDialog.cs \
  tests/SbMac.Tests/UpdateNotificationUiTests.cs
git commit -m "feat(update): notify about available releases"
```

---

### Task 5: Documentation and End-to-End Verification

**Files:**
- Modify: `README.md:12-25`

**Interfaces:**
- Consumes: the completed notification behavior.
- Produces: user-facing install documentation and final verification evidence.

- [ ] **Step 1: Document the behavior under Install**

Add this paragraph after the release-download table in `README.md`:

```markdown
SB-Mac checks for a newer stable release when it opens, at most once every 24
hours. If one is available, **View Release** opens its GitHub page; **Later**
defers the same notification for another day. The app never downloads or
installs an update itself.
```

- [ ] **Step 2: Run release-automation regression tests**

Run:

```bash
bash tests/next-release-version.sh
bash tests/release-workflow.sh
```

Expected: both scripts print their passing summaries and exit 0.

- [ ] **Step 3: Run the full Release test suite**

Run:

```bash
dotnet test --configuration Release --verbosity normal
```

Expected: every existing and new xUnit/Avalonia test passes with zero failures.

- [ ] **Step 4: Build the complete solution in Release mode**

Run:

```bash
dotnet build --configuration Release --no-restore
```

Expected: build succeeds with zero errors.

- [ ] **Step 5: Verify the packaged bundle still contains a valid stamped version**

Run:

```bash
./build/make-app.sh
```

Expected: the script reaches `Built:` and reports the created
`artifacts/SB-Mac.app`; its existing checks confirm a Mach-O executable,
ad-hoc signature, bundle identifier, and bundle version.

- [ ] **Step 6: Inspect the final change set**

Run:

```bash
git status --short
git diff --check
git diff --stat HEAD
```

Expected: only the update feature, its tests, and README are changed; `git diff
--check` prints nothing.

- [ ] **Step 7: Commit the documentation**

```bash
git add README.md
git commit -m "docs: explain update notifications"
```

- [ ] **Step 8: Confirm the final history and clean worktree**

Run:

```bash
git log -5 --oneline
git status --short
```

Expected: the five feature commits are present in task order and `git status
--short` prints nothing.
