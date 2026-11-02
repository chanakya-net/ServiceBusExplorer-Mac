using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Tree;
using SbMac.App.Views;
using SbMac.Core.Connections;
using SbMac.Core.Entities;
using Xunit;

namespace SbMac.Tests;

/// <summary>Keyboard order follows on-screen order inside each workspace region.</summary>
public partial class UiSmokeTests
{
    sealed record FocusStop(string Label, double X, string Region);

    static readonly string[] RegionOrder =
        ["MainToolbar", "Sidebar", "MessageHeader", "MessageGrid", "Inspector", "StatusBar"];

    static (MainWindow Window, MainWindowViewModel Vm, RecordingUi Recorder) OpenPopulatedWorkspace(int width, int height, bool dark)
    {
        var vm = NewIsolatedViewModel();
        var ns = new NamespaceNodeViewModel(new NamespaceConnection { Name = "fixture",
            AuthenticationMode = AuthenticationMode.ConnectionString, ConnectionString = EventHubFixtures.NamespaceConnectionString });
        RecordingUi.AttachOfflineMessages(ns);
        var queue = new QueueNodeViewModel(new QueueEntity(Model<Azure.Messaging.ServiceBus.Administration.QueueProperties>("orders"), null))
            { Parent = new QueueFolderNodeViewModel { Parent = ns } };
        vm.Namespaces.Add(ns);
        var window = new MainWindow { DataContext = vm, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        var (ui, recorder) = RecordingUi.Create();
        vm.Ui = ui;
        vm.SelectedNode = queue;
        for (var i = 1; i <= 3; i++) vm.Messages.Add(BuildMessageRow(i));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, recorder);
    }

    static string? RegionOf(MainWindow window, Visual visual)
    {
        foreach (var control in visual.GetSelfAndVisualAncestors().OfType<Control>())
        {
            switch (control.Name)
            {
                case "MainToolbar" or "MessageHeader" or "MessageGrid": return control.Name;
                case "EntitySearchBox" or "EntityTree" or "FirstRunState": return "Sidebar";
                case "DetailsSplitter" or "MessageInspector": return "Inspector";
                case "ActivityToggle": return "StatusBar";
            }
        }
        return null;
    }

    static string LabelOf(Visual visual) => visual.GetSelfAndVisualAncestors().OfType<Control>()
        .Select(c => c.Name ?? AutomationProperties.GetName(c))
        .FirstOrDefault(n => !string.IsNullOrEmpty(n) && !n.StartsWith("PART_")) ?? visual.GetType().Name;

    static List<FocusStop> WalkFocus(MainWindow window, Control start, bool backwards, int presses)
    {
        start.Focus(NavigationMethod.Tab);
        var stops = new List<FocusStop>();
        for (var i = 0; i < presses; i++)
        {
            if (window.FocusManager?.GetFocusedElement() is Visual focused && RegionOf(window, focused) is { } region)
            {
                var x = focused.TranslatePoint(new Point(), window)?.X ?? double.NaN;
                var label = LabelOf(focused);
                if (stops.Count == 0 || stops[^1].Label != label) stops.Add(new FocusStop(label, x, region));
            }
            Press(window, Key.Tab, backwards ? RawInputModifiers.Shift : RawInputModifiers.None);
        }
        return stops;
    }

    static void AssertVisualOrder(List<FocusStop> stops, string region, bool backwards)
    {
        var inRegion = stops.Where(s => s.Region == region).ToList();
        var trace = string.Join(" -> ", stops.Select(s => $"{s.Label}@{s.X:0}"));
        Assert.True(inRegion.Count >= 3, trace);
        for (var i = 1; i < inRegion.Count; i++)
            Assert.True(backwards ? inRegion[i].X < inRegion[i - 1].X : inRegion[i].X > inRegion[i - 1].X, trace);
    }

    [AvaloniaTheory]
    [InlineData(1360, 860, false)]
    [InlineData(960, 600, true)]
    public void ToolbarAndHeaderTabOrderFollowsVisualOrder(int width, int height, bool dark)
    {
        var (window, vm, _) = OpenPopulatedWorkspace(width, height, dark);
        try
        {
            window.GetControl<DataGrid>("MessageGrid").SelectedItems.Add(vm.Messages[0]);
            window.UpdateLayout();
            var toolbarStart = window.GetControl<DockPanel>("MainToolbar").Children.OfType<Button>().First();
            foreach (var backwards in new[] { false, true })
            {
                var start = backwards ? window.GetControl<Button>("ActionsButton") : toolbarStart;
                AssertVisualOrder(WalkFocus(window, start, backwards, 8), "MainToolbar", backwards);
            }
            var compact = window.HeaderCompact;
            var forward = WalkFocus(window, window.GetControl<Button>("SelectAllMessagesButton"), false, 10);
            AssertVisualOrder(forward, "MessageHeader", false);
            var header = forward.Where(s => s.Region == "MessageHeader").Select(s => s.Label).ToList();
            Assert.Equal(compact ? "MessageActionsButton" : "ColumnsButton", header[^1]);
            AssertVisualOrder(WalkFocus(window, window.GetControl<Button>(header[^1]), true, 10), "MessageHeader", true);
            Assert.Single(vm.SelectedMessages);
        }
        finally { window.Close(); }
    }

    static List<string> RegionSequence(List<FocusStop> stops)
    {
        var regions = new List<string>();
        foreach (var stop in stops)
            if (regions.Count == 0 || regions[^1] != stop.Region) regions.Add(stop.Region);
        return regions;
    }

    [AvaloniaTheory]
    [InlineData(1360, 860, false)]
    [InlineData(960, 600, true)]
    public void MainWorkspaceKeyboardTraversalReachesNamedRegions(int width, int height, bool dark)
    {
        var (window, vm, _) = OpenPopulatedWorkspace(width, height, dark);
        try
        {
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, window.ActualThemeVariant);
            var grid = window.GetControl<DataGrid>("MessageGrid");
            grid.SelectedItems.Add(vm.Messages[1]);
            var peek = window.GetControl<DockPanel>("MainToolbar").Children.OfType<Button>().First();
            var forward = RegionSequence(WalkFocus(window, peek, false, 40));
            Assert.Equal(RegionOrder, forward.Take(RegionOrder.Length));
            var backward = RegionSequence(WalkFocus(window, window.GetControl<Button>("ActivityToggle"), true, 40));
            Assert.Equal(RegionOrder.Reverse(), backward.Take(RegionOrder.Length));
            Assert.Equal([vm.Messages[1]], vm.SelectedMessages);
            Assert.Equal("Fetch count", AutomationProperties.GetName(window.GetControl<NumericUpDown>("FetchCountInput")));
            Assert.Equal("Messages", AutomationProperties.GetName(grid));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MessageGridArrowKeysMoveRowAndSpaceTogglesOnlyFocusedCheckbox()
    {
        var (window, vm, _) = OpenPopulatedWorkspace(1360, 860, false);
        try
        {
            var grid = window.GetControl<DataGrid>("MessageGrid");
            grid.SelectedItem = vm.Messages[0];
            grid.Focus(NavigationMethod.Tab);
            Press(window, Key.Down);
            Assert.Same(vm.Messages[1], grid.SelectedItem);
            Assert.Same(vm.Messages[1], vm.SelectedMessage);
            Assert.Equal("message-2", vm.BodyText);
            var focused = Assert.IsAssignableFrom<Control>(window.FocusManager?.GetFocusedElement());
            Assert.True(grid.IsKeyboardFocusWithin);
            Assert.Contains(":focus-visible", focused.Classes);
            var before = vm.Messages.Select(m => m.IsSelected).ToArray();
            var box = grid.GetVisualDescendants().OfType<CheckBox>().Single(c => ReferenceEquals(c.DataContext, vm.Messages[2]));
            box.Focus(NavigationMethod.Tab);
            Press(window, Key.Space);
            var after = vm.Messages.Select(m => m.IsSelected).ToArray();
            Assert.Equal(new[] { before[0], before[1], !before[2] }, after);
            Assert.Contains(vm.Messages[2], vm.SelectedMessages);
            Press(window, Key.Space);
            Assert.Equal(before, vm.Messages.Select(m => m.IsSelected));
            Assert.DoesNotContain(vm.Messages[2], vm.SelectedMessages);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("ActionsButton", "Copy body")]
    [InlineData("ColumnsButton", null)]
    [InlineData("MessageActionsButton", "Inspector")]
    public void WorkspaceMenusSupportKeyboardActivationAndReturnFocus(string name, string? activate)
    {
        var (window, vm, recorder) = OpenPopulatedWorkspace(960, 600, false);
        try
        {
            var grid = window.GetControl<DataGrid>("MessageGrid");
            grid.SelectedItem = vm.Messages[0];
            var selection = vm.SelectedMessages.ToArray();
            var button = window.GetControl<Button>(name);
            Assert.True(button.IsEffectivelyVisible);
            button.Focus(NavigationMethod.Tab);
            Press(window, Key.Enter);
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            Assert.True(menu.IsOpen);
            var items = menu.Items.OfType<MenuItem>().ToList();
            var target = activate is null ? items[0] : items.Single(i => Equals(i.Header, activate));
            for (var i = 0; i < items.Count + 2 && !target.IsSelected; i++) Press(window, Key.Down);
            Assert.True(target.IsSelected);
            var columns = vm.MessageColumns.ToArray();
            var inspector = vm.IsInspectorVisible;
            Press(window, Key.Enter);
            Assert.False(menu.IsOpen);
            if (name == "ActionsButton") Assert.Equal(["message-1"], recorder.ClipboardTexts);
            if (name == "ColumnsButton") Assert.False(columns.SequenceEqual(vm.MessageColumns));
            // Hiding the inspector deliberately hands focus to the message grid.
            if (name == "MessageActionsButton") Assert.NotEqual(inspector, vm.IsInspectorVisible);
            Assert.True((name == "MessageActionsButton" ? (Control)grid : button).IsKeyboardFocusWithin);
            button.Focus(NavigationMethod.Tab);
            Press(window, Key.Enter);
            Assert.True(menu.IsOpen);
            Press(window, Key.Escape);
            Assert.False(menu.IsOpen);
            Assert.True(button.IsKeyboardFocusWithin);
            Assert.Equal(selection, vm.SelectedMessages);
        }
        finally { window.Close(); }
    }
}
