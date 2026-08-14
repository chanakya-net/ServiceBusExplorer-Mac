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
        Assert.Null(await new JsonUpdateCheckStateStore(SettingsFile)
            .GetLastCheckUtcAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TimestampRoundTripsAsUtc()
    {
        var store = new JsonUpdateCheckStateStore(SettingsFile);
        var timestamp = new DateTimeOffset(2026, 8, 14, 4, 30, 0, TimeSpan.Zero);

        await store.SetLastCheckUtcAsync(timestamp, TestContext.Current.CancellationToken);

        Assert.Equal(timestamp, await store.GetLastCheckUtcAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UpdatingTimestampPreservesUnknownSettings()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            SettingsFile,
            """{"theme":"system","futureFlag":true}""",
            TestContext.Current.CancellationToken);

        await new JsonUpdateCheckStateStore(SettingsFile)
            .SetLastCheckUtcAsync(
                new DateTimeOffset(2026, 8, 14, 4, 30, 0, TimeSpan.Zero),
                TestContext.Current.CancellationToken);

        var root = Assert.IsType<JsonObject>(JsonNode.Parse(
            await File.ReadAllTextAsync(SettingsFile, TestContext.Current.CancellationToken)));
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
        await File.WriteAllTextAsync(SettingsFile, json, TestContext.Current.CancellationToken);

        Assert.Null(await new JsonUpdateCheckStateStore(SettingsFile)
            .GetLastCheckUtcAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SavingReplacesInvalidJsonWithValidState()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(SettingsFile, "not json", TestContext.Current.CancellationToken);
        var timestamp = new DateTimeOffset(2026, 8, 14, 4, 30, 0, TimeSpan.Zero);

        var store = new JsonUpdateCheckStateStore(SettingsFile);
        await store.SetLastCheckUtcAsync(timestamp, TestContext.Current.CancellationToken);

        Assert.Equal(timestamp, await store.GetLastCheckUtcAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RelativeFilenameSavesInCurrentDirectory()
    {
        var fileName = $"settings-{Guid.NewGuid():N}.json";
        var timestamp = new DateTimeOffset(2026, 8, 14, 4, 30, 0, TimeSpan.Zero);

        try
        {
            var store = new JsonUpdateCheckStateStore(fileName);

            await store.SetLastCheckUtcAsync(timestamp, TestContext.Current.CancellationToken);

            Assert.True(File.Exists(fileName));
            Assert.Equal(timestamp, await store.GetLastCheckUtcAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }

    [Fact]
    public async Task WriteFailureDoesNotEscape()
    {
        Directory.CreateDirectory(directory);
        var fileWhereDirectoryIsNeeded = Path.Combine(directory, "blocked");
        await File.WriteAllTextAsync(
            fileWhereDirectoryIsNeeded,
            "content",
            TestContext.Current.CancellationToken);

        var store = new JsonUpdateCheckStateStore(Path.Combine(fileWhereDirectoryIsNeeded, "settings.json"));

        await store.SetLastCheckUtcAsync(DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
