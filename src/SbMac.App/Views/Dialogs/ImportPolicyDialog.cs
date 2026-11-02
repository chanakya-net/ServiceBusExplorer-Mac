using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using SbMac.Core.ImportExport;

namespace SbMac.App.Views.Dialogs;

/// <summary>
/// Asks how an import should treat entities that already exist, before anything is
/// written to the namespace.
/// </summary>
/// <remarks>
/// The namespace summary scrolls inside a capped area so a long source name cannot push the
/// policy choices or the action buttons off screen. Skip stays the default choice, and
/// Cancel, Escape and closing the window all return null without importing anything.
/// </remarks>
public sealed class ImportPolicyDialog : Window
{
    const double MaxSummaryHeight = 240;

    ImportPolicyDialog(string summary)
    {
        Title = "Import entities";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var skip = CreateChoice("Skip entities that already exist", isChecked: true);
        var update = CreateChoice("Update entities that already exist");
        var fail = CreateChoice("Report a conflict and leave existing entities alone");

        var cancel = CreateCancelButton();
        var import = CreateImportButton(skip, update, fail);

        var scroller = new ScrollViewer
        {
            Content = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 },
            MaxHeight = MaxSummaryHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Opened += (_, _) =>
        {
            ClampToScreen(scroller);
            skip.Focus(Avalonia.Input.NavigationMethod.Tab);
        };

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
                scroller,
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

    static RadioButton CreateChoice(string text, bool isChecked = false) => new()
    {
        Content = text,
        GroupName = "policy",
        IsChecked = isChecked
    };

    Button CreateCancelButton()
    {
        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Classes.Add("secondary");
        cancel.Click += (_, _) => Close(null);
        AutomationProperties.SetName(cancel, "Cancel: import nothing");
        return cancel;
    }

    Button CreateImportButton(RadioButton skip, RadioButton update, RadioButton fail)
    {
        var import = new Button { Content = "Import", MinWidth = 88, IsDefault = true };
        import.Classes.Add("primary");
        import.Click += (_, _) => Close(SelectedPolicy(skip, update, fail));
        AutomationProperties.SetName(import, "Import: apply the selected conflict policy");
        return import;
    }

    static ImportConflictPolicy SelectedPolicy(RadioButton skip, RadioButton update, RadioButton fail) =>
        update.IsChecked == true ? ImportConflictPolicy.Update :
        fail.IsChecked == true ? ImportConflictPolicy.Fail :
        ImportConflictPolicy.Skip;

    /// <summary>Caps the summary to the space left on screen so the policy choices and buttons stay reachable.</summary>
    void ClampToScreen(ScrollViewer scroller)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var frame = FrameSize ?? ClientSize;
        var available = screen.WorkingArea.Height / screen.Scaling - (frame.Height - ClientSize.Height);
        scroller.MaxHeight = Math.Max(0, Math.Min(MaxSummaryHeight, available - 320));
        MaxHeight = Math.Max(0, available);
    }

    /// <summary>Returns the chosen policy, or null when the user cancels.</summary>
    public static async Task<ImportConflictPolicy?> ShowAsync(Window owner, string summary)
    {
        var dialog = new ImportPolicyDialog(summary);
        return await dialog.ShowDialog<ImportConflictPolicy?>(owner);
    }
}
