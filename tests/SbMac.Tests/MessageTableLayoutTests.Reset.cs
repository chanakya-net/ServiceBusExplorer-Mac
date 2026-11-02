using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SbMac.App.Models;
using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Tree;
using SbMac.Core.EventHubs;
using Xunit;

namespace SbMac.Tests;

public partial class UiSmokeTests
{
    [AvaloniaFact]
    public async Task MessageTableLayout_ResetRestoresEveryContextImmediatelyAndAfterRestart()
    {
        using var fixture = new TableLayoutFixture();
        foreach (var context in Enum.GetValues<MessageTableContext>())
        {
            SelectResetContext(fixture.Vm, context);
            fixture.Vm.SetMessageColumns(fixture.Vm.MessageColumns.Select(c => c with
                { IsVisible = c.Id != "sequence" && c.IsVisible, Width = 250 }).Reverse().ToArray());
        }
        SelectResetContext(fixture.Vm, MessageTableContext.ServiceBus);
        Assert.DoesNotContain("SEQ", VisibleHeaders(fixture.Grid));
        Assert.True(await fixture.Preferences.ResetLayoutAsync());
        SettleTable(fixture.Window);
        AssertDefaultTables(fixture);
        Assert.False(fixture.Preferences.HasPendingSaves);
        fixture.Restart();
        AssertDefaultTables(fixture);
        Assert.False(fixture.Preferences.HasPendingSaves);
    }

    static void SelectResetContext(MainWindowViewModel vm, MessageTableContext context)
    {
        vm.SelectedNode = context == MessageTableContext.EventHub
            ? new EventHubNodeViewModel(new EventHubEntity("events", DateTimeOffset.UnixEpoch, ["0"], ["$Default"])) : null;
        vm.ShowDeadLetter = context == MessageTableContext.DeadLetter;
    }

    static void AssertDefaultTables(TableLayoutFixture fixture)
    {
        foreach (var context in Enum.GetValues<MessageTableContext>())
        {
            SelectResetContext(fixture.Vm, context);
            var expected = MessageTableLayoutViewModel.ForContext(context);
            Assert.Equal(expected, fixture.Vm.MessageColumns);
            string[] headers = context switch
            {
                MessageTableContext.EventHub => ["✓", "PARTITION", "SEQ", "OFFSET", "ENQUEUED", "SIZE", "PREVIEW"],
                MessageTableContext.DeadLetter => ["✓", "SEQ", "DEAD-LETTER REASON", "TRY", "ENQUEUED", "MESSAGE ID", "LABEL", "SIZE", "PREVIEW"],
                _ => ["✓", "SEQ", "ENQUEUED", "MESSAGE ID", "LABEL", "SIZE", "PREVIEW"]
            };
            Assert.Equal(headers, VisibleHeaders(fixture.Grid));
            Assert.Equal(88, fixture.Grid.Columns[1].Width.Value);
            Assert.True(fixture.Grid.Columns[8].Width.IsStar);
            var byId = MessageTableLayoutViewModel.ForContext(MessageTableContext.ServiceBus)
                .Zip(fixture.Grid.Columns).ToDictionary(pair => pair.First.Id, pair => pair.Second);
            Assert.Equal(Enumerable.Range(0, fixture.Grid.Columns.Count), expected.Select(c => byId[c.Id].DisplayIndex));
            var menu = Assert.IsType<MenuFlyout>(fixture.Window.GetControl<Button>("ColumnsButton").Flyout);
            Assert.True(menu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "SEQ").IsChecked);
        }
    }
}
