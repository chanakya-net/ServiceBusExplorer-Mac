using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

using AvaloniaEdit;
using AvaloniaEdit.Highlighting;

using SbMac.App.ViewModels;
using SbMac.App.Views;
using SbMac.App.Views.Controls;
using SbMac.Core.Messaging;

using Xunit;

namespace SbMac.Tests;

/// <summary>The read-only message body viewer: highlighting, fallbacks, copy routing and live theming.</summary>
public partial class UiSmokeTests
{
    const string JsonBody = "{\"name\": \"orders\", \"count\": 42, \"ok\": true}";
    const string XmlBody = "<order id=\"7\"><!-- c --><name>x</name></order>";

    static MessageRowViewModel BodyRow(string body) => new(new MessageRecord
    {
        Body = BinaryData.FromString(body),
        SourceEntityPath = "orders",
        SequenceNumber = 1
    });

    static MessageRowViewModel BodyRow(byte[] body) => new(new MessageRecord
    {
        Body = BinaryData.FromBytes(body),
        SourceEntityPath = "orders",
        SequenceNumber = 1
    });

    static (MainWindow Window, MainWindowViewModel Vm, MessageBodyViewer Viewer, RecordingUi Recorder) OpenBody(MessageRowViewModel row)
    {
        var vm = NewIsolatedViewModel();
        var (ui, recorder) = RecordingUi.Create();
        var window = new MainWindow { DataContext = vm };
        window.Show();
        vm.Ui = ui;
        vm.Messages.Add(row);
        window.GetControl<DataGrid>("MessageGrid").SelectedItem = row;
        Dispatcher.UIThread.RunJobs();
        return (window, vm, window.GetControl<MessageBodyViewer>("BodyViewer"), recorder);
    }

    static HashSet<Color> TokenColors(MessageBodyViewer viewer)
    {
        var definition = viewer.Editor.SyntaxHighlighting;
        var colors = new HashSet<Color>();
        if (definition is null) return colors;
        var highlighter = new DocumentHighlighter(viewer.Editor.Document, definition);
        for (var line = 1; line <= viewer.Editor.Document.LineCount; line++)
            foreach (var section in highlighter.HighlightLine(line).Sections)
                if (section.Color?.Foreground?.GetBrush(null) is ISolidColorBrush brush)
                    colors.Add(brush.Color);
        return colors;
    }

    [AvaloniaTheory]
    [InlineData(JsonBody, "JSON")]
    [InlineData(XmlBody, "XML")]
    public void JsonAndXmlBodiesAreReadOnlyAndHighlighted(string body, string label)
    {
        var (window, vm, viewer, _) = OpenBody(BodyRow(body));

        Assert.Equal(label, vm.BodyFormatLabel);
        Assert.Equal(vm.BodyText, viewer.Editor.Text);
        Assert.True(viewer.Editor.IsReadOnly);
        Assert.True(TokenColors(viewer).Count >= 3, "expected several distinct token colours");
        window.Close();
    }

    [AvaloniaFact]
    public void TextHexAndEmptyBodiesShowUnhighlightedText()
    {
        foreach (var row in new[] { BodyRow("just words"), BodyRow(new byte[] { 0xFF, 0xFE, 0x00, 0x80 }), BodyRow("") })
        {
            var (window, vm, viewer, _) = OpenBody(row);
            Assert.Equal(vm.BodyText, viewer.Editor.Text);
            Assert.Empty(TokenColors(viewer));
            Assert.True(viewer.Editor.IsReadOnly);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ViewerRefusesEditsAndFollowsSelectionChanges()
    {
        var (window, vm, viewer, _) = OpenBody(BodyRow(JsonBody));
        viewer.Editor.TextArea.PerformTextInput("zzz");
        Assert.Equal(vm.BodyText, viewer.Editor.Text);

        var next = BodyRow(XmlBody);
        vm.Messages.Add(next);
        window.GetControl<DataGrid>("MessageGrid").SelectedItem = next;
        Assert.Equal(vm.BodyText, viewer.Editor.Text);
        Assert.Equal("XML", vm.BodyFormatLabel);
        window.Close();
    }

    [AvaloniaFact]
    public void ExplicitCopyAlwaysCopiesTheFullBodyEvenWithSelection()
    {
        var (window, vm, viewer, recorder) = OpenBody(BodyRow(JsonBody));
        viewer.Editor.Select(vm.BodyText.IndexOf("name", StringComparison.Ordinal), 4);
        Assert.Equal("name", viewer.SelectedText);

        vm.CopyBodyCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal([vm.BodyText], recorder.ClipboardTexts);
        window.Close();
    }

    [AvaloniaFact]
    public void MenuCopyCopiesSelectionWhenPresentAndFullBodyOtherwise()
    {
        var (window, vm, viewer, recorder) = OpenBody(BodyRow(JsonBody));
        viewer.Editor.Select(vm.BodyText.IndexOf("name", StringComparison.Ordinal), 4);
        vm.CopySelectionOrBodyCommand.Execute(null);
        viewer.Editor.Select(0, 0);
        vm.CopySelectionOrBodyCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["name", vm.BodyText], recorder.ClipboardTexts);
        window.Close();
    }

    [AvaloniaFact]
    public void HighlightingColoursFollowTheLiveThemeVariant()
    {
        var (window, _, viewer, _) = OpenBody(BodyRow(JsonBody));
        CurrentApp.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        var light = TokenColors(viewer);
        CurrentApp.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        var dark = TokenColors(viewer);
        CurrentApp.RequestedThemeVariant = ThemeVariant.Default;

        Assert.NotEmpty(light);
        Assert.NotEmpty(dark);
        Assert.False(light.SetEquals(dark));
        window.Close();
    }

    [AvaloniaFact]
    public void DetachedViewerStopsReactingToThemeChanges()
    {
        var viewer = new MessageBodyViewer { Text = JsonBody, Format = "JSON" };
        var window = new Window { Content = viewer };
        window.Show();
        window.Content = null;
        Assert.False(viewer.IsThemeSubscribed);
        window.Close();
    }
    [AvaloniaFact]
    public void FindNavigatesLiteralMatchesAndRefreshesWhenBodyChanges()
    {
        var (window, vm, viewer, _) = OpenBody(BodyRow("Alpha aLPHa [alpha]"));
        viewer.OpenFind();
        viewer.FindQuery.Text = "alpha";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("1 of 3", viewer.FindStatus.Text);
        Assert.Equal("Alpha", viewer.SelectedText);
        viewer.FindNext();
        Assert.Equal("aLPHa", viewer.SelectedText);
        viewer.FindPrevious();
        Assert.Equal("Alpha", viewer.SelectedText);
        viewer.FindQuery.Text = "[alpha]";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("1 of 1", viewer.FindStatus.Text);
        var next = BodyRow("nothing here");
        vm.Messages.Add(next);
        window.GetControl<DataGrid>("MessageGrid").SelectedItem = next;
        Assert.Equal("No matches", viewer.FindStatus.Text);
        Assert.Equal("", viewer.SelectedText);
        window.Close();
    }

    [AvaloniaFact]
    public void EscapeDismissesFindAndRestoresEditorFocus()
    {
        var (window, _, viewer, _) = OpenBody(BodyRow("sample"));
        viewer.OpenFind();
        viewer.FindQuery.RaiseEvent(new Avalonia.Input.KeyEventArgs
        {
            RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent,
            Key = Avalonia.Input.Key.Escape
        });
        Assert.False(viewer.FindBar.IsVisible);
        Assert.True(viewer.Editor.TextArea.IsKeyboardFocusWithin);
        window.Close();
    }

}
