using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
