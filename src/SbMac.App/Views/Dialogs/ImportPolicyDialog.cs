using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using SbMac.Core.ImportExport;

namespace SbMac.App.Views.Dialogs;

/// <summary>
/// Asks how an import should treat entities that already exist, before anything is
/// written to the namespace.
/// </summary>
public sealed class ImportPolicyDialog : Window
{
    ImportPolicyDialog(string summary)
    {
        Title = "Import entities";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var skip = new RadioButton
        {
            Content = "Skip entities that already exist",
            GroupName = "policy",
            IsChecked = true
        };

        var update = new RadioButton
        {
            Content = "Update entities that already exist",
            GroupName = "policy"
        };

        var fail = new RadioButton
        {
            Content = "Report a conflict and leave existing entities alone",
            GroupName = "policy"
        };

        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var import = new Button { Content = "Import", MinWidth = 88, IsDefault = true };
        import.Click += (_, _) => Close(
            update.IsChecked == true ? ImportConflictPolicy.Update :
            fail.IsChecked == true ? ImportConflictPolicy.Fail :
            ImportConflictPolicy.Skip);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Import entity definitions",
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 },
                new StackPanel { Spacing = 7, Children = { skip, update, fail } },
                new TextBlock
                {
                    Text = "Settings fixed at creation time — partitioning, sessions and duplicate " +
                           "detection — are never changed on an existing entity.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.6,
                    FontSize = 12
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, import }
                }
            }
        };
    }

    /// <summary>Returns the chosen policy, or null when the user cancels.</summary>
    public static async Task<ImportConflictPolicy?> ShowAsync(Window owner, string summary)
    {
        var dialog = new ImportPolicyDialog(summary);
        return await dialog.ShowDialog<ImportConflictPolicy?>(owner);
    }
}
