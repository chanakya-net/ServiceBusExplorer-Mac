using System.Text.Json.Nodes;

using SbMac.App.Models;
using SbMac.App.Services;

using Xunit;

namespace SbMac.Tests;

public sealed class AppPreferencesTests : IDisposable
{
    static readonly DateTimeOffset Timestamp = new(2026, 8, 14, 4, 30, 0, TimeSpan.Zero);

    readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("sbmac-preferences-");

    string SettingsFile => Path.Combine(directory.FullName, "settings.json");

    static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(AppTheme.System, "system")]
    [InlineData(AppTheme.Light, "light")]
    [InlineData(AppTheme.Dark, "dark")]
    public async Task SavedThemeIsRestoredByAFreshService(AppTheme theme, string persisted)
    {
        var first = new AppPreferencesService(new JsonSettingsStore(SettingsFile));
        first.Load();

        Assert.True(await first.SetThemeAsync(theme));

        var restarted = new AppPreferencesService(new JsonSettingsStore(SettingsFile));
        Assert.Equal(theme, restarted.Load().Theme);
        var root = ReadRoot();
        Assert.Equal(persisted, root["theme"]!.GetValue<string>());
        Assert.Equal(AppPreferences.SchemaVersion, root["schemaVersion"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"theme":"sepia"}""")]
    [InlineData("""{"theme":42}""")]
    [InlineData("""{"lastUpdateCheckUtc":"2026-08-14T04:30:00.0000000Z"}""")]
    public void MissingOrUnrecognizedThemeDefaultsToSystemWithoutRewriting(string? json)
    {
        if (json is not null)
        {
            File.WriteAllText(SettingsFile, json);
        }

        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile)).Load();

        Assert.Equal(AppTheme.System, preferences.Theme);
        Assert.Equal(json, File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile) : null);
    }

    [Fact]
    public async Task ThemeAndUpdateCheckWritesPreserveEachOther()
    {
        File.WriteAllText(SettingsFile, """{"futureFlag":{"nested":[1,{"deep":true}]}}""");
        var themeEntered = new TaskCompletionSource();
        var releaseTheme = new TaskCompletionSource();
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile, async _ =>
        {
            themeEntered.TrySetResult();
            await releaseTheme.Task;
        }));

        // The update checker addresses the same file through a relative path.
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, SettingsFile);
        var timestamps = new JsonUpdateCheckStateStore(new JsonSettingsStore(relative));

        var themeSave = preferences.SetThemeAsync(AppTheme.Dark);
        await themeEntered.Task.WaitAsync(Token);
        var timestampSave = timestamps.SetLastCheckUtcAsync(Timestamp, Token);
        releaseTheme.SetResult();
        Assert.True(await themeSave.WaitAsync(Token));
        await timestampSave.WaitAsync(Token);

        var root = ReadRoot();
        Assert.Equal("dark", root["theme"]!.GetValue<string>());
        Assert.Equal(Timestamp, await timestamps.GetLastCheckUtcAsync(Token));
        Assert.True(root["futureFlag"]!["nested"]![1]!["deep"]!.GetValue<bool>());
    }

    [Fact]
    public async Task TimestampWriteInProgressIsNotOverwrittenByAThemeChange()
    {
        var timestampEntered = new TaskCompletionSource();
        var releaseTimestamp = new TaskCompletionSource();
        var timestamps = new JsonUpdateCheckStateStore(new JsonSettingsStore(SettingsFile, async _ =>
        {
            timestampEntered.TrySetResult();
            await releaseTimestamp.Task;
        }));
        var alias = Path.Combine(directory.FullName, "nested", "..", "settings.json");
        var preferences = new AppPreferencesService(new JsonSettingsStore(alias));

        var timestampSave = timestamps.SetLastCheckUtcAsync(Timestamp, Token);
        await timestampEntered.Task.WaitAsync(Token);
        var themeSave = preferences.SetThemeAsync(AppTheme.Light);
        releaseTimestamp.SetResult();
        await timestampSave.WaitAsync(Token);
        Assert.True(await themeSave.WaitAsync(Token));

        Assert.Equal("light", ReadRoot()["theme"]!.GetValue<string>());
        Assert.Equal(Timestamp, await timestamps.GetLastCheckUtcAsync(Token));
    }

    [Fact]
    public async Task SavingThemeKeepsNewerSchemaAndUnknownValues()
    {
        File.WriteAllText(SettingsFile,
            """{"schemaVersion":7,"lastUpdateCheckUtc":"2026-08-14T04:30:00.0000000Z","list":[1,2],"obj":{"a":"b"}}""");
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile));

        Assert.True(await preferences.SetThemeAsync(AppTheme.Light));

        var root = ReadRoot();
        Assert.Equal(7, root["schemaVersion"]!.GetValue<int>());
        Assert.Equal("light", root["theme"]!.GetValue<string>());
        Assert.Equal("[1,2]", root["list"]!.ToJsonString());
        Assert.Equal("b", root["obj"]!["a"]!.GetValue<string>());
        Assert.Equal(Timestamp, await new JsonUpdateCheckStateStore(SettingsFile).GetLastCheckUtcAsync(Token));
    }

    [Fact]
    public async Task RapidChangesPersistTheLastChoice()
    {
        var release = new TaskCompletionSource();
        var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile, _ => release.Task));
        var announced = new List<AppTheme>();
        preferences.Changed += (_, value) => announced.Add(value.Theme);

        var saves = new[]
        {
            preferences.SetThemeAsync(AppTheme.Light),
            preferences.SetThemeAsync(AppTheme.Dark),
            preferences.SetThemeAsync(AppTheme.System)
        };

        // Memory and listeners change before anything reaches disk.
        Assert.Equal([AppTheme.Light, AppTheme.Dark, AppTheme.System], announced);
        Assert.Equal(AppTheme.System, preferences.Current.Theme);
        Assert.True(preferences.HasPendingSaves);
        Assert.False(File.Exists(SettingsFile));

        release.SetResult();
        Assert.True(await preferences.FlushAsync().WaitAsync(Token));
        Assert.All(saves, save => Assert.True(save.IsCompletedSuccessfully && save.Result));
        Assert.False(preferences.HasPendingSaves);
        Assert.Equal("system", ReadRoot()["theme"]!.GetValue<string>());
    }

    [Fact]
    public async Task FailedSaveKeepsTheInMemoryChoiceAndReportsTheError()
    {
        File.WriteAllText(Path.Combine(directory.FullName, "blocked"), "a file, not a directory");
        var preferences = new AppPreferencesService(
            new JsonSettingsStore(Path.Combine(directory.FullName, "blocked", "settings.json")));
        preferences.Load();

        Assert.False(await preferences.SetThemeAsync(AppTheme.Dark));

        Assert.Equal(AppTheme.Dark, preferences.Current.Theme);
        Assert.NotNull(preferences.LastSaveError);
        Assert.False(await preferences.FlushAsync());
    }

    [Fact]
    public async Task FailedMutationLeavesThePreviousDocument()
    {
        const string original = """{"theme":"dark"}""";
        File.WriteAllText(SettingsFile, original);
        var store = new JsonSettingsStore(SettingsFile);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.UpdateAsync(_ => throw new InvalidOperationException("mutation failed"), Token));

        AssertUnchanged(original);
    }

    [Fact]
    public async Task CancellationBeforePublishLeavesThePreviousDocument()
    {
        const string original = """{"theme":"dark"}""";
        File.WriteAllText(SettingsFile, original);
        using var cancellation = new CancellationTokenSource();
        var store = new JsonSettingsStore(SettingsFile, _ =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.UpdateAsync(root => root["theme"] = "light", cancellation.Token));

        AssertUnchanged(original);
    }

    [Fact]
    public async Task PublicationFailureLeavesThePreviousDocument()
    {
        const string original = """{"theme":"dark"}""";
        File.WriteAllText(SettingsFile, original);
        var store = new JsonSettingsStore(SettingsFile, _ => throw new IOException("disk full"));

        await Assert.ThrowsAsync<IOException>(() => store.UpdateAsync(root => root["theme"] = "light", Token));

        AssertUnchanged(original);
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task UnreadableDocumentIsNeverOverwritten()
    {
        const string original = """{"theme":"dark","keep":true}""";
        File.WriteAllText(SettingsFile, original);
        File.SetUnixFileMode(SettingsFile, UnixFileMode.None);
        try
        {
            var preferences = new AppPreferencesService(new JsonSettingsStore(SettingsFile));

            Assert.Equal(AppTheme.System, preferences.Load().Theme);
            Assert.False(await preferences.SetThemeAsync(AppTheme.Light));
            Assert.Equal(AppTheme.Light, preferences.Current.Theme);
        }
        finally
        {
            File.SetUnixFileMode(SettingsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        AssertUnchanged(original);
    }

    void AssertUnchanged(string original)
    {
        Assert.Equal(original, File.ReadAllText(SettingsFile));
        Assert.Equal(["settings.json"], directory.GetFiles().Select(file => file.Name));
    }

    public void Dispose() => directory.Delete(recursive: true);

    JsonObject ReadRoot() => Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(SettingsFile)));
}
