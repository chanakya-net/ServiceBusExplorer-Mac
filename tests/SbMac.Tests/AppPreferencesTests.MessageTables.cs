using SbMac.App.Models;
using SbMac.App.Services;
using Xunit;

namespace SbMac.Tests;

public sealed partial class AppPreferencesTests
{
    [Fact]
    public async Task ColumnFieldsMergeWithoutLosingFutureDataAndResetRemovesOverrides()
    {
        File.WriteAllText(SettingsFile, """{"schemaVersion":9,"lastUpdateCheckUtc":"2026-08-14T04:30:00Z","messageTables":{"future":{"x":1},"serviceBus":{"other":2,"columns":[{"id":"sequence","visible":true,"future":{"a":4},"width":100},{"id":"futureColumn","visible":false,"future":7}]}}}""");
        var service = new AppPreferencesService(new JsonSettingsStore(SettingsFile));
        service.Load();
        Assert.True(await service.SetMessageColumnsAsync("serviceBus", [new("sequence", true, 2, 123)]));
        var root = ReadRoot();
        var columns = root["messageTables"]!["serviceBus"]!["columns"]!;
        Assert.Equal(4, columns[0]!["future"]!["a"]!.GetValue<int>());
        Assert.Equal("futureColumn", columns[1]!["id"]!.GetValue<string>());
        Assert.Equal(123, columns[0]!["width"]!.GetValue<double>());
        Assert.Equal(9, root["schemaVersion"]!.GetValue<int>());
        Assert.True(await service.SetMessageColumnsAsync("serviceBus", [new("sequence", true)]));
        Assert.Null(ReadRoot()["messageTables"]!["serviceBus"]!["columns"]![0]!["width"]);
        Assert.Equal(Timestamp, await new JsonUpdateCheckStateStore(SettingsFile).GetLastCheckUtcAsync(Token));
    }

    [Theory]
    [InlineData("-1", "0", null, null)]
    [InlineData("1.5", "-2", null, null)]
    [InlineData("2147483648", "\"wide\"", null, null)]
    [InlineData("{}", "1e309", null, null)]
    [InlineData("2", "1e9", 2, 10000d)]
    [InlineData("3", "0.5", 3, 20d)]
    public void OptionalColumnScalarsAreValidatedIndependently(string index, string width, int? expectedIndex, double? expectedWidth)
    {
        var json = "{\"messageTables\":{\"serviceBus\":{\"columns\":[{\"id\":\"sequence\",\"visible\":false,\"displayIndex\":" + index + ",\"width\":" + width + "}]}}}";
        File.WriteAllText(SettingsFile, json);
        var state = new AppPreferencesService(new JsonSettingsStore(SettingsFile)).Load().MessageTables["serviceBus"].Single();
        Assert.False(state.Visible);
        Assert.Equal(expectedIndex, state.DisplayIndex);
        Assert.Equal(expectedWidth, state.Width);
        Assert.Equal(json, File.ReadAllText(SettingsFile));
    }

    [Fact]
    public async Task NonFiniteWidthsNeverReachJsonAndFailureKeepsSessionState()
    {
        var service = new AppPreferencesService(new JsonSettingsStore(SettingsFile));
        Assert.True(await service.SetMessageColumnsAsync("serviceBus", [new("sequence", true, 1, double.NaN), new("preview", true, 2, double.PositiveInfinity)]));
        Assert.All(new AppPreferencesService(new JsonSettingsStore(SettingsFile)).Load().MessageTables["serviceBus"], c => Assert.Null(c.Width));
        var blocked = Path.Combine(directory.FullName, "blocked-columns");
        File.WriteAllText(blocked, "file");
        var failed = new AppPreferencesService(new JsonSettingsStore(Path.Combine(blocked, "settings.json")));
        Assert.False(await failed.SetMessageColumnsAsync("deadLetter", [new("reason", true, 1, 210)]));
        Assert.Equal(210, failed.Current.MessageTables["deadLetter"].Single().Width);
        Assert.False(await failed.FlushAsync());
    }
}
