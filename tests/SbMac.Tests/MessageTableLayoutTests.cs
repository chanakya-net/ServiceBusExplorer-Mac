using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SbMac.App.ViewModels;
using SbMac.App.Views;
using SbMac.App.ViewModels.Tree;
using SbMac.Core.EventHubs;
using SbMac.Core.Messaging;
using Xunit;

namespace SbMac.Tests;

public partial class UiSmokeTests
{
    [AvaloniaFact]
    public void DeadLetterLayoutChangesColumnsWithoutChangingSelection()
    {
        var vm = NewIsolatedViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var grid = window.GetControl<DataGrid>("MessageGrid");
        var row = BuildMessageRow(1);
        vm.Messages.Add(row);
        grid.SelectedItems.Add(row);
        Assert.Equal(new[] { "✓", "SEQ", "ENQUEUED", "MESSAGE ID", "LABEL", "SIZE", "PREVIEW" }, VisibleHeaders(grid));

        vm.ShowDeadLetter = true;
        Assert.Equal(new[] { "✓", "SEQ", "DEAD-LETTER REASON", "TRY", "ENQUEUED", "MESSAGE ID", "LABEL", "SIZE", "PREVIEW" }, VisibleHeaders(grid));
        Assert.Contains(row, vm.SelectedMessages);
        Assert.Same(row, vm.SelectedMessage);
        vm.ShowDeadLetter = false;
        Assert.Contains(row, vm.SelectedMessages);
        Assert.Equal("PREVIEW", VisibleHeaders(grid)[^1]);
        window.Close();
    }

    [Fact]
    public void LayoutIdsAreStableAndDeadLetterDetailsRemainAvailable()
    {
        var ordinary = MessageTableLayoutViewModel.ForContext(MessageTableContext.ServiceBus);
        var dead = MessageTableLayoutViewModel.ForContext(MessageTableContext.DeadLetter);
        var events = MessageTableLayoutViewModel.ForContext(MessageTableContext.EventHub);
        Assert.Equal(ordinary.Select(c => c.Id).Order(), dead.Select(c => c.Id).Order());
        Assert.Equal(ordinary.Select(c => c.Id).Order(), events.Select(c => c.Id).Order());
        Assert.Equal(new[] { "selection", "partition", "sequence", "offset", "enqueued", "size", "preview" },
            events.Where(c => c.IsVisible).Select(c => c.Id));
        Assert.DoesNotContain(ordinary.Concat(dead), c => c.IsVisible && c.Id is "partition" or "offset");
        var row = new MessageRowViewModel(new MessageRecord {
            Body = BinaryData.FromString("body"), SourceEntityPath = "orders", IsDeadLetter = true,
            DeadLetterReason = "reason", DeadLetterErrorDescription = "full description" });
        Assert.Contains(row.PropertyEntries, p => p.Name == "Reason" && p.Value == "reason");
        Assert.Contains(row.PropertyEntries, p => p.Name == "Description" && p.Value == "full description");
    }

    [AvaloniaFact]
    public void EventHubLayoutShowsPartitionAndOffsetAndHidesBrokerColumns()
    {
        var vm = NewIsolatedViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var grid = window.GetControl<DataGrid>("MessageGrid");
        var row = new MessageRowViewModel(new MessageRecord {
            Body = BinaryData.FromString("{}"), SourceEntityPath = "telemetry", SequenceNumber = 7,
            EnqueuedTime = DateTimeOffset.UtcNow, Event = new EventOrigin("3", "112233") });
        vm.Messages.Add(row);
        grid.SelectedItems.Add(row);

        vm.SelectedNode = new EventHubNodeViewModel(
            new EventHubEntity("telemetry", DateTimeOffset.UtcNow, ["0", "3"], ["$Default"]));
        Assert.Equal(new[] { "✓", "PARTITION", "SEQ", "OFFSET", "ENQUEUED", "SIZE", "PREVIEW" }, VisibleHeaders(grid));
        Assert.Equal("112233", row.Offset);
        Assert.Contains(row, vm.SelectedMessages);

        vm.SelectedNode = null;
        Assert.Equal(new[] { "✓", "SEQ", "ENQUEUED", "MESSAGE ID", "LABEL", "SIZE", "PREVIEW" }, VisibleHeaders(grid));
        Assert.Contains(row, vm.SelectedMessages);
        window.Close();
    }

    [Fact]
    public void ServiceBusRowsHaveNoOffset() =>
        Assert.Null(new MessageRowViewModel(new MessageRecord { Body = BinaryData.FromString("x"), SourceEntityPath = "q" }).Offset);

    static string[] VisibleHeaders(DataGrid grid) => grid.Columns.Where(c => c.IsVisible)
        .OrderBy(c => c.DisplayIndex).Select(c => c.Header!.ToString()!).ToArray();
}
