using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using SbMac.App.ViewModels;
using SbMac.App.Models;
using SbMac.App.Services;
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

    [AvaloniaFact]
    public void ColumnsChooserChangesOnlyCurrentContextAndRestoresDefaults()
    {
        var vm = NewIsolatedViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var grid = window.GetControl<DataGrid>("MessageGrid");
        var chooser = window.GetControl<Button>("ColumnsButton");
        var menu = Assert.IsType<MenuFlyout>(chooser.Flyout);
        var sequence = menu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "SEQ");
        sequence.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.DoesNotContain("SEQ", VisibleHeaders(grid));
        vm.ShowDeadLetter = true;
        Assert.Contains("SEQ", VisibleHeaders(grid));
        vm.ShowDeadLetter = false;
        Assert.DoesNotContain("SEQ", VisibleHeaders(grid));
        var restore = menu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "Restore Default Columns");
        restore.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Contains("SEQ", VisibleHeaders(grid));
        window.Close();
    }

    [AvaloniaFact]
    public async Task ColumnChoicesSurviveRestartForEachContext()
    {
        var directory = Directory.CreateTempSubdirectory("sbmac-columns-test-");
        try
        {
            var file = Path.Combine(directory.FullName, "settings.json");
            var preferences = new AppPreferencesService(new JsonSettingsStore(file));
            preferences.Load();
            var vm = NewIsolatedViewModel(preferences);
            vm.SetMessageColumns(vm.MessageColumns.Select(c => c.Id == "sequence" ? c with { IsVisible = false } : c).ToArray());
            vm.ShowDeadLetter = true;
            vm.SetMessageColumns(vm.MessageColumns.Select(c => c.Id == "reason" ? c with { IsVisible = false } : c).ToArray());
            vm.SelectedNode = new EventHubNodeViewModel(new EventHubEntity("events", DateTimeOffset.UtcNow, ["0"], ["$Default"]));
            vm.SetMessageColumns(vm.MessageColumns.Select(c => c.Id == "offset" ? c with { IsVisible = false } : c).ToArray());
            Assert.True(await preferences.FlushAsync());
            var restartedPreferences = new AppPreferencesService(new JsonSettingsStore(file));
            restartedPreferences.Load();
            var restarted = NewIsolatedViewModel(restartedPreferences);
            Assert.False(restarted.MessageColumns.Single(c => c.Id == "sequence").IsVisible);
            restarted.ShowDeadLetter = true;
            Assert.False(restarted.MessageColumns.Single(c => c.Id == "reason").IsVisible);
            Assert.True(restarted.MessageColumns.Single(c => c.Id == "sequence").IsVisible);
            restarted.SelectedNode = new EventHubNodeViewModel(new EventHubEntity("events", DateTimeOffset.UtcNow, ["0"], ["$Default"]));
            Assert.False(restarted.MessageColumns.Single(c => c.Id == "offset").IsVisible);
            restarted.RestoreMessageColumns();
            Assert.True(restarted.MessageColumns.Single(c => c.Id == "offset").IsVisible);
            restarted.SelectedNode = null;
            restarted.ShowDeadLetter = false;
            Assert.False(restarted.MessageColumns.Single(c => c.Id == "sequence").IsVisible);
            Assert.True(await restartedPreferences.FlushAsync());
        }
        finally { directory.Delete(recursive: true); }
    }

    [AvaloniaFact]
    public void LastVisibleDataColumnCannotBeHiddenInChooser()
    {
        var vm = NewIsolatedViewModel();
        vm.SetMessageColumns(vm.MessageColumns.Select(c => c with { IsVisible = c.Id is "selection" or "sequence" }).ToArray());
        var window = new MainWindow { DataContext = vm };
        window.Show();
        var menu = Assert.IsType<MenuFlyout>(window.GetControl<Button>("ColumnsButton").Flyout);
        Assert.False(menu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == "SEQ").IsEnabled);
        Assert.Equal(new[] { "✓", "SEQ" }, VisibleHeaders(window.GetControl<DataGrid>("MessageGrid")));
        window.Close();
    }

    [Fact]
    public void SavedVisibilityIgnoresInvalidIdsAndKeepsADataColumn()
    {
        var saved = new[] { new MessageColumnState("selection", false),
            new MessageColumnState("sequence", false), new MessageColumnState("sequence", true),
            new MessageColumnState("unknown", true) };
        var layout = MessageTableLayoutViewModel.Resolve(MessageTableContext.ServiceBus, saved);
        Assert.True(layout.Single(c => c.Id == "selection").IsVisible);
        Assert.False(layout.Single(c => c.Id == "sequence").IsVisible);
        Assert.DoesNotContain(layout, c => c.Id == "unknown");
        Assert.False(MessageTableLayoutViewModel.Resolve(MessageTableContext.ServiceBus,
            [new MessageColumnState("partition", true)]).Single(c => c.Id == "partition").IsVisible);
        Assert.Contains(layout, c => c.Id != "selection" && c.IsVisible);
        var hidden = MessageTableLayoutViewModel.ForContext(MessageTableContext.ServiceBus)
            .Select(c => new MessageColumnState(c.Id, false)).ToArray();
        Assert.Contains(MessageTableLayoutViewModel.Resolve(MessageTableContext.ServiceBus, hidden),
            c => c.Id != "selection" && c.IsVisible);
    }

    [Fact]
    public void ServiceBusRowsHaveNoOffset() =>
        Assert.Null(new MessageRowViewModel(new MessageRecord { Body = BinaryData.FromString("x"), SourceEntityPath = "q" }).Offset);

    static string[] VisibleHeaders(DataGrid grid) => grid.Columns.Where(c => c.IsVisible)
        .OrderBy(c => c.DisplayIndex).Select(c => c.Header!.ToString()!).ToArray();
}
