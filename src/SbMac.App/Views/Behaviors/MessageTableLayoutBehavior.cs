using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SbMac.App.ViewModels;

namespace SbMac.App.Views.Behaviors;

public sealed class MessageTableLayoutBehavior : IDisposable
{
    bool applying;
    bool capturing;
    bool cancelledGesture;
    DataGridColumn? resizeColumn;
    DataGridLength originalWidth;
    double startingWidth;
    bool widthChanged;
    MainWindowViewModel? viewModel;
    MainWindowViewModel? gestureViewModel;
    MessageTableContext gestureContext;
    readonly DataGrid grid;
    readonly Button button;
    readonly Dictionary<string, DataGridColumn> columns;
    readonly Dictionary<string, DataGridLength> defaults;

    public MessageTableLayoutBehavior(DataGrid grid, Button button)
    {
        this.grid = grid;
        this.button = button;
        columns = MessageTableLayoutViewModel.ColumnIds
            .Select((id, index) => (id, column: grid.Columns[index]))
            .ToDictionary(item => item.id, item => item.column);
        defaults = columns.ToDictionary(c => c.Key, c => c.Value.Width);
        grid.ColumnReordered += OnColumnReordered;
        foreach (var column in columns.Values) column.PropertyChanged += OnColumnPropertyChanged;
        grid.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        grid.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        grid.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void Apply(MainWindowViewModel vm)
    {
        if (!capturing)
        {
            if (gestureViewModel is not null) cancelledGesture = true;
            CancelGesture();
        }
        viewModel = vm;
        var layout = vm.MessageColumns;
        if (!capturing)
        {
            applying = true;
            try { ApplyColumns(layout); }
            finally { applying = false; }
        }
        BuildMenu((MenuFlyout)button.Flyout!, columns, layout, vm);
    }

    void ApplyColumns(IReadOnlyList<MessageColumnMetadata> layout)
    {
        for (var i = 0; i < layout.Count; i++)
        {
            var column = columns[layout[i].Id];
            column.IsVisible = layout[i].IsVisible;
            column.DisplayIndex = i;
            var width = layout[i].Width is { } pixels
                ? new DataGridLength(Math.Clamp(pixels, Math.Max(grid.MinColumnWidth, column.MinWidth), Math.Min(grid.MaxColumnWidth, column.MaxWidth)))
                : defaults[layout[i].Id];
            if (column.Width != width) column.Width = width;
        }
    }

    void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        CancelGesture();
        cancelledGesture = false;
        if (applying || viewModel is null || !e.GetCurrentPoint(grid).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual source ||
            source.GetSelfAndVisualAncestors().OfType<DataGridColumnHeader>().FirstOrDefault() is not { } header) return;
        gestureViewModel = viewModel;
        gestureContext = viewModel.MessageTableContext;
        BeginResize(header, e);
    }

    void BeginResize(DataGridColumnHeader header, PointerPressedEventArgs e)
    {
        var column = columns.Values.FirstOrDefault(c => Equals(c.Header, header.Content));
        if (column is null) return;
        var x = e.GetPosition(header).X;
        var target = header.Bounds.Width - x <= 5 ? column
            : x <= 5 ? columns.Values.Where(c => c.IsVisible && c.DisplayIndex < column.DisplayIndex)
                .OrderBy(c => c.DisplayIndex).LastOrDefault() : null;
        if (target is null || !grid.CanUserResizeColumns || target.CanUserResize == false) return;
        resizeColumn = target;
        originalWidth = target.Width;
        startingWidth = target.ActualWidth;
        // DataGrid's star resize path uses proportional units. Convert only the explicit
        // edge target before its header processes the press; a click restores star sizing.
        if (target.Width.IsStar && double.IsFinite(startingWidth) && startingWidth > 0)
        {
            applying = true;
            try { target.Width = new DataGridLength(startingWidth); }
            finally { applying = false; }
        }
    }

    void OnColumnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!applying && sender == resizeColumn && e.Property == DataGridColumn.WidthProperty)
            widthChanged = true;
    }

    void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (cancelledGesture)
        {
            cancelledGesture = false;
            if (viewModel is not null) Apply(viewModel);
            return;
        }
        if (gestureViewModel == viewModel && gestureContext == viewModel?.MessageTableContext &&
            resizeColumn is { } column && widthChanged && double.IsFinite(column.ActualWidth) &&
            column.ActualWidth > 0 && Math.Abs(column.ActualWidth - startingWidth) > 0.5)
        {
            var id = columns.First(c => c.Value == column).Key;
            var pixels = column.ActualWidth;
            resizeColumn = null;
            column.Width = new DataGridLength(pixels);
            CaptureLayout(id, pixels);
        }
        CancelGesture();
    }

    void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // A context switch invalidates the header's in-flight gesture as well as its save.
        if (cancelledGesture) e.Handled = true;
    }

    void OnColumnReordered(object? sender, DataGridColumnEventArgs e)
    {
        if (applying || viewModel is null) return;
        if (gestureViewModel != viewModel || gestureContext != viewModel.MessageTableContext)
        {
            Apply(viewModel);
            return;
        }
        CaptureLayout();
    }

    void CaptureLayout(string? resizedId = null, double? pixels = null)
    {
        if (viewModel is null) return;
        var previous = viewModel.MessageColumns;
        var snapshot = previous.OrderBy(c => columns[c.Id].DisplayIndex)
            .Select(c => c.Id == resizedId ? c with { Width = pixels } : c).ToArray();
        if (snapshot.SequenceEqual(previous)) return;
        capturing = true;
        try { viewModel.SetMessageColumns(snapshot); }
        finally { capturing = false; }
    }

    public void CancelGesture()
    {
        if (resizeColumn is { } column)
        {
            applying = true;
            try { column.Width = originalWidth; }
            finally { applying = false; }
        }
        resizeColumn = null;
        widthChanged = false;
        gestureViewModel = null;
    }

    public void ClearContext()
    {
        if (gestureViewModel is not null) cancelledGesture = true;
        CancelGesture();
        viewModel = null;
    }

    public void Dispose()
    {
        ClearContext();
        grid.ColumnReordered -= OnColumnReordered;
        foreach (var column in columns.Values) column.PropertyChanged -= OnColumnPropertyChanged;
        grid.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
        grid.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
        grid.RemoveHandler(InputElement.PointerMovedEvent, OnPointerMoved);
    }

    static void BuildMenu(MenuFlyout menu, Dictionary<string, DataGridColumn> columns,
        IReadOnlyList<MessageColumnMetadata> layout, MainWindowViewModel vm)
    {
        menu.Items.Clear();
        var visibleCount = layout.Count(c => c.Id != "selection" && c.IsVisible);
        foreach (var column in layout.Where(c => MessageTableLayoutViewModel.IsApplicable(vm.MessageTableContext, c.Id)))
        {
            var item = new MenuItem { Header = columns[column.Id].Header, ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = column.IsVisible, IsEnabled = !column.IsVisible || visibleCount > 1 };
            item.Click += (_, _) => vm.SetMessageColumns(layout.Select(c => c.Id == column.Id
                ? c with { IsVisible = !column.IsVisible } : c).ToArray());
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var restore = new MenuItem { Header = "Restore Default Columns" };
        restore.Click += (_, _) => vm.RestoreMessageColumns();
        menu.Items.Add(restore);
    }

}
