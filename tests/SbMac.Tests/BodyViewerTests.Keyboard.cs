using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using SbMac.App.Views.Dialogs;
using SbMac.App.ViewModels.Dialogs;
using Xunit;

namespace SbMac.Tests;

public partial class UiSmokeTests
{
    [AvaloniaFact]
    public void BodyFindKeyboardPathRemainsScopedAndEscapable()
    {
        var (window, _, viewer, _) = OpenBody(BodyRow("alpha ALPHA alpha"));
        try
        {
            var search = window.GetControl<TextBox>("EntitySearchBox");
            search.Focus(NavigationMethod.Tab);
            Press(window, Key.F, RawInputModifiers.Meta);
            Assert.False(viewer.FindBar.IsVisible);
            viewer.OpenFindButton.Focus(NavigationMethod.Tab);
            Press(window, Key.Tab);
            Assert.True(viewer.Editor.TextArea.IsFocused);
            Press(window, Key.F, RawInputModifiers.Meta);
            Assert.True(viewer.FindQuery.IsFocused);
            window.KeyTextInput("alpha");
            Assert.Equal("1 of 3", viewer.FindStatus.Text);
            Press(window, Key.Enter);
            Assert.Equal("2 of 3", viewer.FindStatus.Text);
            Press(window, Key.Enter, RawInputModifiers.Shift);
            Assert.Equal("1 of 3", viewer.FindStatus.Text);
            Press(window, Key.Escape);
            Assert.False(viewer.FindBar.IsVisible);
            Assert.True(viewer.Editor.TextArea.IsFocused);
            Press(window, Key.Tab);
            Assert.False(viewer.Editor.IsKeyboardFocusWithin);
            viewer.Editor.TextArea.Focus(NavigationMethod.Tab);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.False(viewer.Editor.IsKeyboardFocusWithin);
            Assert.Equal("Message body text", AutomationProperties.GetName(viewer.Editor.TextArea));
            Assert.Equal("alpha ALPHA alpha", viewer.Editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NativeCopyHonorsFindPasswordAndOwnedDialog()
    {
        var (window, _, viewer, recorder) = OpenBody(BodyRow(JsonBody));
        try
        {
            var copy = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>()
                .Single(i => i.Header == "Messages").Menu!.Items.OfType<NativeMenuItem>()
                .Single(i => i.Gesture?.Key == Key.C);
            void Copy() { ((INativeMenuItemExporterEventsImplBridge)copy).RaiseClicked(); Dispatcher.UIThread.RunJobs(); }
            viewer.OpenFind();
            viewer.FindQuery.Text = "name";
            viewer.FindQuery.SelectAll();
            Copy();
            Assert.Equal("name", await window.Clipboard!.TryGetTextAsync());
            var pending = window.EditConnectionAsync(null);
            var dialog = Assert.IsType<ConnectionDialog>(Assert.Single(window.OwnedWindows));
            var name = dialog.GetControl<TextBox>("NameField");
            name.Focus(); name.Text = "owned fixture"; name.SelectAll();
            Copy();
            Assert.Equal("owned fixture", await window.Clipboard!.TryGetTextAsync());
            var password = new TextBox { PasswordChar = '•', Text = "fixture-secret" };
            var passwordWindow = new Window { Content = password };
            _ = passwordWindow.ShowDialog<object?>(dialog);
            try
            {
                password.Focus(); password.SelectAll(); Copy();
                Assert.Equal("owned fixture", await window.Clipboard!.TryGetTextAsync());
            }
            finally { passwordWindow.Close(); }
            Press(dialog, Key.Escape);
            Assert.Null(await pending);
            Assert.Empty(recorder.ClipboardTexts);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task NativeCopyHonorsPropertiesAndHiddenInspector()
    {
        var (window, vm, viewer, recorder) = OpenBody(BodyRow(JsonBody));
        try
        {
            var copy = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>()
                .Single(i => i.Header == "Messages").Menu!.Items.OfType<NativeMenuItem>()
                .Single(i => i.Gesture?.Key == Key.C);
            var property = new SelectableTextBlock { Text = "property-fixture", Focusable = true };
            var dialog = new Window { Content = property };
            _ = dialog.ShowDialog<object?>(window);
            property.Focus(); property.SelectionStart = 0; property.SelectionEnd = 8;
            ((INativeMenuItemExporterEventsImplBridge)copy).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("property", await window.Clipboard!.TryGetTextAsync());
            dialog.Close();
            viewer.Editor.Select(2, 4);
            vm.IsInspectorVisible = false;
            Dispatcher.UIThread.RunJobs();
            ((INativeMenuItemExporterEventsImplBridge)copy).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([vm.BodyText], recorder.ClipboardTexts);
        }
        finally { window.Close(); }
    }

}
