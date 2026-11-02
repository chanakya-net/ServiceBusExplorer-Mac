using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Threading;

namespace SbMac.App.Views;

public partial class MainWindow
{
    void MoveActivityFocus()
    {
        // Opening from the status toggle enters the panel. Background operation updates
        // and saved layout restoration must not steal focus from another workflow.
        if (ViewModel?.IsActivityExpanded == true)
        {
            if (!ActivityToggle.IsKeyboardFocusWithin) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (ViewModel?.IsActivityExpanded == true && ActivityToggle.IsKeyboardFocusWithin)
                    ActivityPanel.GetControl<Button>("HideActivityButton").Focus(NavigationMethod.Tab);
            });
        }
        else if (ActivityPanel.IsKeyboardFocusWithin)
            ActivityToggle.Focus(NavigationMethod.Tab);
    }

    void OnCopyFocusedControl(object? sender, EventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is not Visual focused) return;
        var topLevel = TopLevel.GetTopLevel(focused);
        if (topLevel != this && (topLevel is not Window dialog || !IsOwnedDialog(dialog))) return;
        var ancestors = focused.GetSelfAndVisualAncestors().ToArray();
        if (ancestors.OfType<TextBox>().FirstOrDefault() is { } textBox)
        {
            // Native menu dispatch bypasses the normal password key guard.
            if (textBox.PasswordChar == default) textBox.Copy();
            return;
        }
        if (ancestors.OfType<SelectableTextBlock>().FirstOrDefault() is { } property)
        {
            property.Copy();
            return;
        }
        if (TopLevel.GetTopLevel(focused) != this) return;
        if (BodyViewer.Editor.IsKeyboardFocusWithin)
            ViewModel?.CopySelectionOrBodyCommand.Execute(null);
        else if (MessageGrid.IsKeyboardFocusWithin)
            ViewModel?.CopyBodyCommand.Execute(null);
    }

    bool IsOwnedDialog(Window dialog)
    {
        for (var owner = dialog.Owner; owner is not null; owner = owner.Owner)
            if (owner == this) return true;
        return false;
    }

    void OnInspectorSplitterKeyDown(object? sender, KeyEventArgs e)
    {
        // The macOS backend can leave forward Tab on the splitter. Enter the
        // selected inspector tab explicitly; retain Shift+Tab and resize arrows.
        if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None ||
            !InspectorTabs.IsEffectivelyVisible) return;
        e.Handled = InspectorTabs.ContainerFromIndex(Math.Max(0, InspectorTabs.SelectedIndex))
            ?.Focus(NavigationMethod.Tab) == true;
    }

    void OnEntityTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.Shift) ||
            (e.Key == Key.Apps && e.KeyModifiers == KeyModifiers.None))
        {
            EntityTree.ContextMenu?.Open(EntityTree);
            e.Handled = true;
        }
    }
}
