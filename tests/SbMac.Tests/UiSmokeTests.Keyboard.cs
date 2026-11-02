using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SbMac.App.Views;
using Xunit;

namespace SbMac.Tests;

public partial class UiSmokeTests
{
    static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, (PhysicalKey)0, null);
        if (window.IsVisible) window.KeyRelease(key, modifiers, (PhysicalKey)0, null);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(1360, 860, false)]
    [InlineData(960, 600, true)]
    public void MainWorkspaceKeyboardTraversalReachesNamedRegions(int width, int height, bool dark)
    {
        var vm = NewIsolatedViewModel();
        var window = new MainWindow { DataContext = vm, Width = width, Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            var search = window.GetControl<TextBox>("EntitySearchBox");
            search.Focus(NavigationMethod.Tab);
            var reached = new HashSet<string>();
            for (var i = 0; i < 60; i++)
            {
                if (window.FocusManager?.GetFocusedElement() is Avalonia.Visual visual)
                    foreach (var control in visual.GetSelfAndVisualAncestors().OfType<Control>())
                        if (control.Name is { } name) reached.Add(name);
                Press(window, Key.Tab, i < 30 ? RawInputModifiers.None : RawInputModifiers.Shift);
                if (i == 29) search.Focus(NavigationMethod.Tab);
            }
            Assert.True(reached.Contains("ActionsButton"), string.Join(",", reached));
            Assert.Contains("ColumnsButton", reached);
            Assert.Contains("FetchCountInput", reached);
            Assert.Equal("Fetch count", AutomationProperties.GetName(window.GetControl<NumericUpDown>("FetchCountInput")));
            Assert.Equal("Messages", AutomationProperties.GetName(window.GetControl<DataGrid>("MessageGrid")));
            Assert.Empty(vm.SelectedMessages);
            search.Focus(NavigationMethod.Tab);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.False(search.IsKeyboardFocusWithin);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData("ActionsButton")]
    [InlineData("ColumnsButton")]
    [InlineData("MessageActionsButton")]
    public void WorkspaceMenusSupportKeyboardActivationAndReturnFocus(string name)
    {
        var vm = NewIsolatedViewModel();
        var window = new MainWindow { DataContext = vm, Width = 960, Height = 600 };
        window.Show();
        try
        {
            var button = window.GetControl<Button>(name);
            Assert.True(button.IsEffectivelyVisible);
            button.Focus(NavigationMethod.Tab);
            Press(window, Key.Enter);
            var menu = Assert.IsType<MenuFlyout>(button.Flyout);
            Assert.True(menu.IsOpen);
            Press(window, Key.Down);
            if (name == "ColumnsButton")
            {
                var before = vm.MessageColumns.ToArray();
                Press(window, Key.Enter);
                Assert.False(before.SequenceEqual(vm.MessageColumns));
                Assert.False(menu.IsOpen);
                Assert.True(button.IsKeyboardFocusWithin);
                Press(window, Key.Enter);
                Assert.True(menu.IsOpen);
            }
            Press(window, Key.Escape);
            Assert.False(menu.IsOpen);
            Assert.True(button.IsKeyboardFocusWithin);
            Assert.Empty(vm.SelectedMessages);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void WorkspaceVisibilityChangesPreserveUsableKeyboardFocus()
    {
        var vm = NewIsolatedViewModel();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            var toggle = window.GetControl<Button>("ActivityToggle");
            toggle.Focus(NavigationMethod.Tab);
            Press(window, Key.Enter);
            Assert.True(vm.IsActivityExpanded);
            var panel = window.GetControl<SbMac.App.Views.Controls.ActivityPanel>("ActivityPanel");
            Assert.True(panel.GetControl<Button>("HideActivityButton").IsKeyboardFocusWithin);
            Press(window, Key.Enter);
            Assert.False(vm.IsActivityExpanded);
            Assert.True(toggle.IsKeyboardFocusWithin);
            vm.IsInspectorVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.GetControl<DataGrid>("MessageGrid").IsKeyboardFocusWithin);
            Assert.Empty(vm.SelectedMessages);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ComposerKeyboardPathLeavesBodyWithoutSubmitting()
    {
        var owner = new MainWindow { DataContext = NewIsolatedViewModel() };
        owner.Show();
        try
        {
            var pending = owner.ComposeMessageAsync(new SbMac.App.ViewModels.Dialogs.SendMessageRequest("orders"));
            var dialog = Assert.IsType<SbMac.App.Views.Dialogs.SendMessageDialog>(Assert.Single(owner.OwnedWindows));
            var body = dialog.GetControl<TextBox>("BodyInput");
            body.Focus(NavigationMethod.Tab);
            dialog.KeyTextInput("draft\tvalue");
            var text = body.Text;
            Press(dialog, Key.Tab);
            Assert.False(body.IsFocused);
            Assert.Equal(text, body.Text);
            Assert.True(dialog.IsVisible);
            Press(dialog, Key.Tab, RawInputModifiers.Shift);
            Assert.True(body.IsFocused);
            Press(dialog, Key.Enter);
            Assert.Contains("\n", body.Text);
            Assert.True(dialog.IsVisible);
            Press(dialog, Key.Escape);
            Assert.Null(await pending);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public void InspectorTabTraversalLeavesSplitterAndReachesBody()
    {
        var (window, _, viewer, _) = OpenBody(BodyRow(JsonBody));
        try
        {
            window.Width = 960; window.Height = 600; window.UpdateLayout();
            window.GetControl<GridSplitter>("DetailsSplitter").Focus(NavigationMethod.Tab);
            Press(window, Key.Tab);
            Assert.True(window.GetControl<TabControl>("InspectorTabs").IsKeyboardFocusWithin);
            for (var i = 0; i < 8 && !viewer.Editor.IsKeyboardFocusWithin; i++) Press(window, Key.Tab);
            Assert.True(viewer.Editor.IsKeyboardFocusWithin);
            Press(window, Key.Tab);
            Assert.True(window.GetControl<Button>("ActivityToggle").IsKeyboardFocusWithin);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TreeContextMenuCanBeOpenedAndDismissedByKeyboard()
    {
        var vm = NewIsolatedViewModel();
        var node = new SbMac.App.ViewModels.Tree.NamespaceNodeViewModel(new SbMac.Core.Connections.NamespaceConnection { Name = "fixture" });
        vm.Namespaces.Add(node);
        var window = new MainWindow { DataContext = vm };
        window.Show();
        try
        {
            var tree = window.GetControl<TreeView>("EntityTree");
            tree.SelectedItem = node;
            var item = Assert.IsType<TreeViewItem>(tree.ContainerFromIndex(0));
            item.Focus(NavigationMethod.Tab);
            Press(window, Key.F10, RawInputModifiers.Shift);
            Assert.True(tree.ContextMenu!.IsOpen);
            Press(window, Key.Escape);
            Assert.False(tree.ContextMenu.IsOpen);
            Assert.True(item.IsFocused);
            Assert.Same(node, vm.SelectedNode);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HeaderCompactionPreservesFocusedAction()
    {
        var window = new MainWindow { DataContext = NewIsolatedViewModel(), Width = 1360, Height = 860 };
        window.Show();
        try
        {
            window.GetControl<Button>("InspectorButton").Focus(NavigationMethod.Tab);
            window.Width = 960; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var more = window.GetControl<Button>("MessageActionsButton");
            Assert.True(more.IsKeyboardFocusWithin);
            window.Width = 970; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.True(more.IsKeyboardFocusWithin);
            window.Width = 1360; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.True(window.GetControl<Button>("InspectorButton").IsKeyboardFocusWithin);
        }
        finally { window.Close(); }
    }

}
