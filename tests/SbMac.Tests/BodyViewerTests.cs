using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
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
    public void WordWrapToggleIsRestoredInNewWindow()
    {
        var dir = Directory.CreateTempSubdirectory("sbmac-wrap-");
        try
        {
            var file = Path.Combine(dir.FullName, "settings.json");
            var first = new SbMac.App.Services.AppPreferencesService(new SbMac.App.Services.JsonSettingsStore(file));
            first.Load();
            var window = new MainWindow();
            window.AttachWorkspacePreferences(first);
            var viewer = window.GetControl<MessageBodyViewer>("BodyViewer");
            viewer.WrapToggle.IsChecked = true;
            Assert.True(first.FlushAsync().GetAwaiter().GetResult());
            var second = new SbMac.App.Services.AppPreferencesService(new SbMac.App.Services.JsonSettingsStore(file));
            second.Load();
            var restarted = new MainWindow();
            restarted.AttachWorkspacePreferences(second);
            var restored = restarted.GetControl<MessageBodyViewer>("BodyViewer");
            Assert.True(restored.WrapToggle.IsChecked);
            Assert.True(restored.Editor.WordWrap);
        }
        finally { dir.Delete(true); }
    }

    [AvaloniaFact]
    public void WordWrapTogglePreservesDocumentSelectionAndCopy()
    {
        var body = string.Concat(Enumerable.Repeat("long body text ", 100));
        var row = BodyRow(body);
        var (window, vm, viewer, recorder) = OpenBody(row);
        Assert.False(viewer.Editor.WordWrap);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, viewer.Editor.HorizontalScrollBarVisibility);
        viewer.Editor.Select(0, 4);
        viewer.WrapToggle.IsChecked = true;
        Assert.True(viewer.Editor.WordWrap);
        Assert.Equal(body, viewer.Editor.Text);
        Assert.Equal("long", viewer.SelectedText);
        vm.CopyBodyCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([body], recorder.ClipboardTexts);
        Assert.Equal(body, vm.BodyText);
        viewer.WrapToggle.IsChecked = false;
        Assert.False(viewer.Editor.WordWrap);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, viewer.Editor.HorizontalScrollBarVisibility);
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

    [AvaloniaFact]
    public async Task NativeCopyUsesActiveFocusedControl()
    {
        var (window, vm, viewer, recorder) = OpenBody(BodyRow(JsonBody));
        try
        {
            var menu = NativeMenu.GetMenu(window)!;
            var messages = menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == "Messages");
            var copy = messages.Menu!.Items.OfType<NativeMenuItem>().Single(i => i.Gesture?.Key == Avalonia.Input.Key.C);
            viewer.Editor.Select(2, 4);
            var search = window.GetControl<TextBox>("EntitySearchBox");
            search.Text = "fixture search";
            search.Focus(Avalonia.Input.NavigationMethod.Tab);
            search.SelectionStart = 0;
            search.SelectionEnd = 7;
            await window.Clipboard!.SetTextAsync("sentinel");
            ((INativeMenuItemExporterEventsImplBridge)copy).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("fixture", await window.Clipboard!.TryGetTextAsync());
            Assert.Empty(recorder.ClipboardTexts);
            search.SelectionEnd = search.SelectionStart;
            ((INativeMenuItemExporterEventsImplBridge)copy).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("fixture", await window.Clipboard!.TryGetTextAsync());
            Assert.Empty(recorder.ClipboardTexts);
            viewer.Editor.TextArea.Focus();
            viewer.Editor.Select(0, 0);
            ((INativeMenuItemExporterEventsImplBridge)copy).RaiseClicked();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal([vm.BodyText], recorder.ClipboardTexts);
        }
        finally { window.Close(); }
    }

    const int Limit = MessageBodyViewer.HighlightByteLimit;

    /// <summary>
    /// Multi-line JSON (one 40-char string per line once pretty-printed) whose displayed text is
    /// exactly <paramref name="bytes"/> UTF-8 bytes; <paramref name="variant"/> makes distinct documents.
    /// </summary>
    static string JsonOfBytes(int bytes, int variant = 0)
    {
        string Build(int pad) => "[" + string.Join(",", Enumerable.Range(0, (bytes - 2) / 50)
            .Select(i => "\"" + variant.ToString("D4") + i.ToString("D6") + new string('x', 30) + "\"")) + ",\"" + new string('p', pad) + "\"]";
        var shown = Encoding.UTF8.GetByteCount(MessageBodyDecoder.Decode(BinaryData.FromString(Build(0))).Text);
        return Build(bytes - shown);
    }

    /// <summary>Single-line JSON string value of the given size: the pathological long-line case.</summary>
    static string LongLineJson(int bytes) => "{\"k\": \"" + new string('x', bytes - 12) + "\"}";

    [AvaloniaFact]
    public void BodyOverOneMiBShowsFullPlainTextWithExplanation()
    {
        var body = JsonOfBytes(Limit + 1);
        var (window, vm, viewer, recorder) = OpenBody(BodyRow(body));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("JSON", vm.BodyFormatLabel);
        Assert.Null(viewer.Editor.SyntaxHighlighting);
        Assert.True(viewer.HighlightNotice.IsVisible);
        Assert.Contains("plain text", viewer.HighlightNotice.Text);
        Assert.Equal(vm.BodyText, viewer.Editor.Text);
        Assert.True(Encoding.UTF8.GetByteCount(vm.BodyText) > Limit);
        vm.CopyBodyCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([vm.BodyText], recorder.ClipboardTexts);
        window.Close();
    }

    [AvaloniaFact]
    public void BodyAtExactlyOneMiBStaysHighlightedWithoutNotice()
    {
        var (window, vm, viewer, _) = OpenBody(BodyRow(JsonOfBytes(Limit)));
        Assert.Equal(Limit, Encoding.UTF8.GetByteCount(vm.BodyText));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(viewer.Editor.SyntaxHighlighting);
        Assert.False(viewer.HighlightNotice.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void ThresholdCountsUtf8BytesNotCharacters()
    {
        var body = "\"" + new string('é', Limit / 2) + "\"";
        Assert.True(Encoding.UTF8.GetByteCount(body) > Limit && body.Length < Limit);
        var (window, _, viewer, _) = OpenBody(BodyRow(body));
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewer.HighlightNotice.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void RapidSelectionLoadsOnlyTheLatestLargeBody()
    {
        var (window, vm, viewer, _) = OpenBody(BodyRow("small"));
        var grid = window.GetControl<DataGrid>("MessageGrid");
        var loadsBefore = viewer.DocumentLoads;
        MessageRowViewModel last = null!;
        for (var i = 0; i < 5; i++)
        {
            last = BodyRow(JsonOfBytes(Limit + 10 + i));
            vm.Messages.Add(last);
            grid.SelectedItem = last;
        }
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(last.BodyText, viewer.Editor.Text);
        Assert.Equal(1, viewer.DocumentLoads - loadsBefore);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectingSmallBodyAfterLargeDiscardsPendingLargeLoad()
    {
        var (window, vm, viewer, _) = OpenBody(BodyRow("small"));
        var grid = window.GetControl<DataGrid>("MessageGrid");
        var big = BodyRow(JsonOfBytes(Limit + 10));
        var tiny = BodyRow("tiny");
        vm.Messages.Add(big); vm.Messages.Add(tiny);
        grid.SelectedItem = big;
        grid.SelectedItem = tiny;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("tiny", viewer.Editor.Text);
        Assert.False(viewer.HighlightNotice.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void LargeBodyFindIsBoundedAndStaleSearchIsDiscarded()
    {
        var body = string.Concat(Enumerable.Repeat("alpha beta ", 60_000));
        var (window, _, viewer, _) = OpenBody(BodyRow(body));
        Dispatcher.UIThread.RunJobs();
        viewer.OpenFind();
        viewer.FindQuery.Text = "alpha";
        viewer.FindQuery.Text = "beta";
        WaitForSearch(viewer);

        Assert.Equal("1 of 10000+", viewer.FindStatus.Text);
        Assert.Equal("beta", viewer.SelectedText);
        viewer.FindQuery.Text = "zzz";
        WaitForSearch(viewer);
        Assert.Equal("No matches", viewer.FindStatus.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void SelectionChangeCancelsInFlightLargeSearch()
    {
        var body = string.Concat(Enumerable.Repeat("alpha ", 120_000));
        var (window, vm, viewer, _) = OpenBody(BodyRow(body));
        Dispatcher.UIThread.RunJobs();
        viewer.OpenFind();
        viewer.FindQuery.Text = "alpha";
        var next = BodyRow("nothing");
        vm.Messages.Add(next);
        window.GetControl<DataGrid>("MessageGrid").SelectedItem = next;
        WaitForSearch(viewer);
        Assert.Equal("No matches", viewer.FindStatus.Text);
        Assert.Equal("", viewer.SelectedText);
        window.Close();
    }

    static void WaitForSearch(MessageBodyViewer viewer)
    {
        var timer = Stopwatch.StartNew();
        do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        while (viewer.IsSearching && timer.Elapsed < TimeSpan.FromSeconds(20));
        Dispatcher.UIThread.RunJobs();
        Assert.False(viewer.IsSearching);
    }

    [AvaloniaFact]
    public void ReturningToDisplayedBodyRestartsCancelledSearch()
    {
        var body = string.Concat(Enumerable.Repeat("alpha beta ", 6000));
        var (window, _, viewer, _) = OpenBody(BodyRow(body));
        Dispatcher.UIThread.RunJobs();
        viewer.FindQuery.Text = "alpha";
        viewer.OpenFind();
        Assert.True(viewer.IsSearching);
        var loads = viewer.DocumentLoads;
        viewer.Text = new string('b', MessageBodyViewer.DeferredLoadChars + 1);
        viewer.Text = body;
        WaitForSearch(viewer);
        Assert.Equal(body, viewer.Editor.Text);
        Assert.Equal(loads, viewer.DocumentLoads);
        Assert.Equal("1 of 6000", viewer.FindStatus.Text);
        viewer.FindNext();
        Assert.Equal("2 of 6000", viewer.FindStatus.Text);
        Assert.Equal("alpha", viewer.SelectedText);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void WrapUsesLogicalLineBoundaries(string delimiter)
    {
        var line = new string('w', 500);
        var body = string.Concat(Enumerable.Repeat(line + delimiter, 500));
        var (window, _, viewer, _) = OpenBody(BodyRow(body));
        Dispatcher.UIThread.RunJobs();
        viewer.WrapToggle.IsChecked = true;
        Assert.True(viewer.Editor.WordWrap);
        Assert.False(viewer.HighlightNotice.IsVisible);
        Assert.False(MessageBodyViewer.HasLineLongerThan(new string('w', MessageBodyViewer.WrapLineLimit) + delimiter + "x", MessageBodyViewer.WrapLineLimit));
        Assert.True(MessageBodyViewer.HasLineLongerThan(new string('w', MessageBodyViewer.WrapLineLimit + 1) + delimiter, MessageBodyViewer.WrapLineLimit));
        window.Close();
    }

    /// <summary>Records timings for local investigation; asserts only correctness, never wall-clock.</summary>
    [AvaloniaFact]
    public void TenThousandRowsShareFewLargeBodiesAndRecordSelectionTimings()
    {
        var shared = Enumerable.Range(0, 4).Select(v => BinaryData.FromString(JsonOfBytes(Limit + 1, v))).ToArray();
        var (window, vm, viewer, _) = OpenBody(BodyRow("seed"));
        for (var i = 0; i < 10_000; i++)
            vm.Messages.Add(new MessageRowViewModel(new MessageRecord { Body = shared[i % 4], SourceEntityPath = "orders", SequenceNumber = i + 2 }));
        var grid = window.GetControl<DataGrid>("MessageGrid");
        var timings = new List<string>();
        for (var i = 1; i <= 12; i++)
        {
            var row = vm.Messages[i * 700 + 1];
            var watch = Stopwatch.StartNew();
            grid.SelectedItem = row;
            Dispatcher.UIThread.RunJobs();
            timings.Add($"selection {i}{(i == 1 ? " (first load)" : " (warm)")}: {watch.Elapsed.TotalMilliseconds:F1} ms");
            Assert.Equal(row.BodyText, viewer.Editor.Text);
        }
        Assert.Equal(10_001, vm.Messages.Count);
        Assert.True(viewer.HighlightNotice.IsVisible);
        var path = Environment.GetEnvironmentVariable("SBMAC_TIMINGS_FILE");
        if (path is not null) File.WriteAllLines(path, timings);
        window.Close();
    }

    [AvaloniaFact]
    public void VeryLongLineDisablesWrapRenderingWithNoticeButKeepsToggleAndText()
    {
        var body = new string('w', MessageBodyViewer.WrapLineLimit + 1);
        var (window, vm, viewer, _) = OpenBody(BodyRow(body));
        viewer.WrapToggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(viewer.WrapToggle.IsChecked);
        Assert.False(viewer.Editor.WordWrap);
        Assert.Contains("wrap", viewer.HighlightNotice.Text);
        Assert.Equal(vm.BodyText, viewer.Editor.Text);
        var (shortWindow, _, shortViewer, _) = OpenBody(BodyRow(new string('w', 500) + "\n" + new string('w', 500)));
        shortViewer.WrapToggle.IsChecked = true;
        Assert.True(shortViewer.Editor.WordWrap);
        Assert.False(shortViewer.HighlightNotice.IsVisible);
        window.Close(); shortWindow.Close();
    }
}
