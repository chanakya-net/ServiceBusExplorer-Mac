using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SbMac.App.Services;
using SbMac.App.ViewModels;
using SbMac.App.Views;
using Xunit;

namespace SbMac.Tests;

public partial class UiSmokeTests
{
    [AvaloniaFact]
    public async Task MessageTableLayout_HeaderReorderSurvivesRestart()
    {
        using var fixture = new TableLayoutFixture();
        var window = fixture.Window;
        var grid = fixture.Grid;
        var rows = new[] { BuildMessageRow(1), BuildMessageRow(2) };
        foreach (var row in rows) { fixture.Vm.Messages.Add(row); grid.SelectedItems.Add(row); }
        var focused = fixture.Vm.SelectedMessage;
        SettleTable(window);
        var from = HeaderPoint(window, "SEQ");
        var to = HeaderPoint(window, "ENQUEUED") + new Vector(35, 0);
        DragTable(window, from, to);
        Assert.Equal(new[] { "✓", "ENQUEUED", "SEQ" }, VisibleHeaders(grid).Take(3));
        Assert.Equal(new[] { "selection", "enqueued", "sequence" }, fixture.Vm.MessageColumns.Take(3).Select(c => c.Id));
        Assert.True(grid.Columns[8].Width.IsStar);
        DragTable(window, HeaderPoint(window, "✓"), HeaderPoint(window, "MESSAGE ID"));
        DragTable(window, HeaderPoint(window, "SEQ"), HeaderPoint(window, "✓"));
        Assert.Equal(0, grid.Columns[0].DisplayIndex);
        Assert.All(rows, row => { Assert.Contains(row, fixture.Vm.SelectedMessages); Assert.True(row.IsSelected); });
        Assert.Same(focused, fixture.Vm.SelectedMessage);
        var expected = VisibleHeaders(grid);
        Assert.True(await fixture.Preferences.FlushAsync());
        fixture.Restart();
        Assert.Equal(expected, VisibleHeaders(fixture.Grid));
        Assert.True(fixture.Grid.Columns[8].Width.IsStar);
    }

    [AvaloniaTheory]
    [InlineData("LABEL", 4)]
    [InlineData("PREVIEW", 8)]
    public async Task MessageTableLayout_HeaderResizeSurvivesRestart(string header, int index)
    {
        using var fixture = new TableLayoutFixture();
        var window = fixture.Window;
        if (header == "PREVIEW")
            DragTable(window, HeaderPoint(window, "PREVIEW"), HeaderPoint(window, "LABEL"));
        var original = fixture.Grid.Columns[index].ActualWidth;
        var writes = 0;
        fixture.Preferences.Changed += (_, _) => writes++;
        var from = HeaderPoint(window, header, rightEdge: true);
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(50, 0));
        SettleTable(window);
        Assert.Equal(0, writes); // No per-pixel writes.
        window.MouseUp(from + new Vector(50, 0), MouseButton.Left);
        SettleTable(window);
        var width = fixture.Grid.Columns[index].ActualWidth;
        Assert.InRange(width, original + 40, original + 60);
        Assert.Equal(width, fixture.Vm.MessageColumns.Single(c => c.Id == (index == 8 ? "preview" : "label")).Width);
        Assert.Equal(1, writes);
        if (index != 8) Assert.True(fixture.Grid.Columns[8].Width.IsStar);
        Assert.True(await fixture.Preferences.FlushAsync());
        fixture.Restart();
        Assert.False(fixture.Grid.Columns[index].Width.IsStar);
        Assert.Equal(width, fixture.Grid.Columns[index].Width.Value, precision: 2);
    }

    [AvaloniaFact]
    public void MessageTableLayout_SortWindowResizeAndNoOpEdgeClickDoNotSaveWidths()
    {
        using var fixture = new TableLayoutFixture();
        var window = fixture.Window;
        var writes = 0;
        fixture.Preferences.Changed += (_, _) => writes++;
        window.Width += 200;
        SettleTable(window);
        foreach (var point in new[] { HeaderPoint(window, "LABEL"), HeaderPoint(window, "PREVIEW", true) })
        {
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            SettleTable(window);
        }
        Assert.Equal(0, writes);
        Assert.True(fixture.Grid.Columns[8].Width.IsStar);
        Assert.All(fixture.Vm.MessageColumns, c => Assert.Null(c.Width));
    }

    static Point HeaderPoint(Window window, string text, bool rightEdge = false)
    {
        var header = window.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .Single(h => h.IsVisible && h.Content?.ToString() == text);
        return header.TranslatePoint(new Point(rightEdge ? header.Bounds.Width - 1 : header.Bounds.Width / 2,
            header.Bounds.Height / 2), window)!.Value;
    }

    static void DragTable(Window window, Point from, Point to)
    {
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        for (var i = 1; i <= 12; i++)
        {
            window.MouseMove(from + (to - from) * (i / 12d));
            SettleTable(window);
        }
        window.MouseUp(to, MouseButton.Left);
        SettleTable(window);
    }

    static void SettleTable(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    sealed class TableLayoutFixture : IDisposable
    {
        readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("sbmac-layout-gesture-");
        public AppPreferencesService Preferences { get; private set; } = null!;
        public MainWindowViewModel Vm { get; private set; } = null!;
        public MainWindow Window { get; private set; } = null!;
        public DataGrid Grid => Window.GetControl<DataGrid>("MessageGrid");

        public TableLayoutFixture() => Restart();

        public void Restart()
        {
            Window?.Close();
            Preferences = new AppPreferencesService(new JsonSettingsStore(Path.Combine(directory.FullName, "settings.json")));
            Preferences.Load();
            Vm = NewIsolatedViewModel(Preferences);
            Window = new MainWindow { DataContext = Vm, Width = 1360, Height = 860 };
            Window.Show();
            SettleTable(Window);
        }

        public void Dispose() { Window.Close(); directory.Delete(true); }
    }
}
