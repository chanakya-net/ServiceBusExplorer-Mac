using Avalonia.Controls;
using SbMac.App.ViewModels;

namespace SbMac.App.Views.Behaviors;

public static class MessageTableLayoutBehavior
{
    public static void Apply(DataGrid grid, Button button, MainWindowViewModel vm)
    {
        var columns = MessageTableLayoutViewModel.ColumnIds
            .Select((id, index) => (id, column: grid.Columns[index]))
            .ToDictionary(item => item.id, item => item.column);
        var layout = vm.MessageColumns;
        for (var i = 0; i < layout.Count; i++)
        {
            columns[layout[i].Id].IsVisible = layout[i].IsVisible;
            columns[layout[i].Id].DisplayIndex = i;
        }
        BuildMenu((MenuFlyout)button.Flyout!, columns, layout, vm);
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
