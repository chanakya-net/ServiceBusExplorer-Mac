using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using SbMac.App.Services;

namespace SbMac.App.Views.Dialogs;

/// <summary>
/// Asks what should happen to the dead-letter originals after a resubmit.
/// </summary>
/// <remarks>
/// This is a three-way choice, not a yes/no: resubmit and keep, resubmit and delete, or
/// don't resubmit at all. Collapsing it into a confirmation would make Cancel mean
/// "resubmit but keep", which is the one thing a cancelling user did not ask for.
/// The safe choice (keep copies) is the default button; deleting is styled as destructive
/// and must be chosen deliberately.
/// </remarks>
public sealed class ResubmitDialog : Window
{
    const double MaxDescriptionHeight = 340;

    ResubmitDialog(int messageCount, string targetName)
    {
        Title = "Resubmit messages";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var cancel = CreateCancelButton();
        var keep = CreateKeepButton();
        var delete = CreateDeleteButton();

        var scroller = new ScrollViewer
        {
            Content = CreateDescription(messageCount, targetName),
            MaxHeight = MaxDescriptionHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Opened += (_, _) =>
        {
            ClampToScreen(scroller);
            keep.Focus(Avalonia.Input.NavigationMethod.Tab);
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Resubmit messages",
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold
                },
                scroller,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, keep, delete }
                }
            }
        };
    }

    Button CreateCancelButton()
    {
        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Classes.Add("secondary");
        cancel.Click += (_, _) => Close(null);
        AutomationProperties.SetName(cancel, "Cancel: do not resubmit");
        return cancel;
    }

    Button CreateKeepButton()
    {
        var keep = new Button { Content = "Resubmit only", MinWidth = 120, IsDefault = true };
        keep.Classes.Add("primary");
        keep.Click += (_, _) => Close(ResubmitAction.ResubmitOnly);
        AutomationProperties.SetName(keep, "Resubmit only: keep the dead-letter copies");
        return keep;
    }

    Button CreateDeleteButton()
    {
        var delete = new Button { Content = "Resubmit and delete", MinWidth = 150 };
        delete.Classes.Add("destructive");
        delete.Click += (_, _) => Close(ResubmitAction.ResubmitAndDelete);
        AutomationProperties.SetName(delete, "Resubmit and delete: remove the dead-letter copies after sending");
        return delete;
    }

    static StackPanel CreateDescription(int messageCount, string targetName) => new()
    {
        Spacing = 10,
        Children =
        {
            new TextBlock
            {
                Text = $"Send {messageCount:N0} message(s) back to “{targetName}”.",
                TextWrapping = TextWrapping.Wrap
            },
            new TextBlock
            {
                Text = "Resubmit only keeps the dead-lettered copies so you can retry again.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                FontSize = 12
            },
            new TextBlock
            {
                Text = "Resubmit and delete removes them once the copies are sent.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                FontSize = 12
            }
        }
    };

    /// <summary>Caps the description to the space left on screen so the buttons stay reachable.</summary>
    void ClampToScreen(ScrollViewer scroller)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var frame = FrameSize ?? ClientSize;
        var available = screen.WorkingArea.Height / screen.Scaling - (frame.Height - ClientSize.Height);
        scroller.MaxHeight = Math.Max(0, Math.Min(MaxDescriptionHeight, available - 160));
        MaxHeight = Math.Max(0, available);
    }

    /// <summary>Returns the chosen action, or null when the user cancels.</summary>
    public static async Task<ResubmitAction?> ShowAsync(Window owner, int messageCount, string targetName)
    {
        var dialog = new ResubmitDialog(messageCount, targetName);
        return await dialog.ShowDialog<ResubmitAction?>(owner);
    }
}
