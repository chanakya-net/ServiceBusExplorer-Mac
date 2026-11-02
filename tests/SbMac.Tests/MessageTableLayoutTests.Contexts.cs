using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Tree;
using SbMac.Core.EventHubs;
using Xunit;

namespace SbMac.Tests;

public partial class UiSmokeTests
{
    [AvaloniaFact]
    public async Task MessageTableLayout_ContextSwitchAndRestorePreserveOtherLayouts()
    {
        using var fixture = new TableLayoutFixture();
        var vm = fixture.Vm;
        DragTable(fixture.Window, HeaderPoint(fixture.Window, "SEQ"), HeaderPoint(fixture.Window, "ENQUEUED") + new Vector(35, 0));
        var edge = HeaderPoint(fixture.Window, "LABEL", true);
        DragTable(fixture.Window, edge, edge + new Vector(45, 0));
        var serviceBus = vm.MessageColumns.ToArray();
        ToggleTableColumn(fixture.Window, "LABEL");
        Assert.False(vm.MessageColumns.Single(c => c.Id == "label").IsVisible);
        ToggleTableColumn(fixture.Window, "LABEL");
        Assert.Equal(serviceBus, vm.MessageColumns);
        vm.ShowDeadLetter = true;
        vm.SetMessageColumns(vm.MessageColumns.OrderBy(c => c.Id == "reason" ? 1 : c.Id == "selection" ? 0 : 2)
            .Select(c => c.Id == "reason" ? c with { Width = 250 } : c).ToArray());
        var deadLetter = vm.MessageColumns.ToArray();
        SelectEventContext(vm);
        vm.SetMessageColumns(vm.MessageColumns.Select(c => c.Id == "offset" ? c with { Width = 205 } : c).ToArray());
        vm.RestoreMessageColumns();
        Assert.Equal(MessageTableLayoutViewModel.ForContext(MessageTableContext.EventHub), vm.MessageColumns);
        Assert.True(await fixture.Preferences.FlushAsync());
        fixture.Restart();
        vm = fixture.Vm;
        Assert.Equal(serviceBus, vm.MessageColumns);
        vm.ShowDeadLetter = true;
        Assert.Equal(deadLetter, vm.MessageColumns);
        SelectEventContext(vm);
        Assert.Equal(MessageTableLayoutViewModel.ForContext(MessageTableContext.EventHub), vm.MessageColumns);
    }

    [AvaloniaFact]
    public void MessageTableLayout_ReapplyDoesNotSaveOrDuplicateGestureHandlers()
    {
        using var fixture = new TableLayoutFixture();
        var writes = 0;
        fixture.Preferences.Changed += (_, _) => writes++;
        for (var i = 0; i < 4; i++)
        {
            fixture.Vm.ShowDeadLetter = true;
            fixture.Vm.ShowDeadLetter = false;
        }
        Assert.Equal(0, writes);
        var edge = HeaderPoint(fixture.Window, "LABEL", true);
        DragTable(fixture.Window, edge, edge + new Vector(35, 0));
        Assert.Equal(1, writes);
    }

    [AvaloniaFact]
    public void MessageTableLayout_ContextChangeDuringGestureCannotSaveOldWidthOrOrder()
    {
        using var fixture = new TableLayoutFixture();
        var window = fixture.Window;
        var edge = HeaderPoint(window, "LABEL", true);
        window.MouseDown(edge, MouseButton.Left);
        window.MouseMove(edge + new Vector(25, 0));
        fixture.Vm.ShowDeadLetter = true;
        window.MouseMove(edge + new Vector(45, 0));
        window.MouseUp(edge + new Vector(45, 0), MouseButton.Left);
        SettleTable(window);
        Assert.Empty(fixture.Preferences.Current.MessageTables);
        Assert.All(fixture.Vm.MessageColumns, c => Assert.Null(c.Width));
        Assert.Equal(112, fixture.Grid.Columns[4].ActualWidth);
        var from = HeaderPoint(window, "SEQ");
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(30, 0));
        fixture.Vm.ShowDeadLetter = false;
        window.MouseUp(HeaderPoint(window, "LABEL"), MouseButton.Left);
        SettleTable(window);
        Assert.Empty(fixture.Preferences.Current.MessageTables);
        Assert.Equal(new[] { "✓", "SEQ", "ENQUEUED" }, VisibleHeaders(fixture.Grid).Take(3));
    }

    [AvaloniaFact]
    public void MessageTableLayout_LeftEdgeResizeAndDataContextReplacementAreIsolated()
    {
        using var fixture = new TableLayoutFixture();
        var window = fixture.Window;
        // Left edge of SIZE belongs to the preceding visible LABEL column.
        var edge = HeaderPoint(window, "SIZE") - new Vector(fixture.Grid.Columns[5].ActualWidth / 2 - 1, 0);
        DragTable(window, edge, edge + new Vector(30, 0));
        Assert.InRange(fixture.Vm.MessageColumns.Single(c => c.Id == "label").Width!.Value, 141, 143);
        Assert.Null(fixture.Vm.MessageColumns.Single(c => c.Id == "size").Width);
        var prior = fixture.Vm.MessageColumns.ToArray();
        var replacement = NewIsolatedViewModel();
        edge = HeaderPoint(window, "LABEL", true);
        window.MouseDown(edge, MouseButton.Left);
        window.MouseMove(edge + new Vector(25, 0));
        window.DataContext = replacement;
        window.MouseUp(edge + new Vector(40, 0), MouseButton.Left);
        SettleTable(window);
        Assert.Equal(prior, fixture.Vm.MessageColumns);
        Assert.All(replacement.MessageColumns, c => Assert.Null(c.Width));
        Assert.Equal(112, fixture.Grid.Columns[4].ActualWidth);
    }

    static void ToggleTableColumn(Window window, string header)
    {
        var menu = (MenuFlyout)window.GetControl<Button>("ColumnsButton").Flyout!;
        menu.Items.OfType<MenuItem>().Single(i => i.Header?.ToString() == header)
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    static void SelectEventContext(MainWindowViewModel vm) => vm.SelectedNode =
        new EventHubNodeViewModel(new EventHubEntity("events", DateTimeOffset.UnixEpoch, ["0"], ["$Default"]));
}
