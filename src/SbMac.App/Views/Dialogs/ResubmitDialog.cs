using Avalonia.Controls;
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
/// </remarks>
public sealed class ResubmitDialog : Window
{
    ResubmitDialog(int messageCount, string targetName)
    {
        Title = "Resubmit messages";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var cancel = new Button { Content = "Cancel", MinWidth = 88, IsCancel = true };
        cancel.Click += (_, _) => Close(null);

        var resubmitOnly = new Button { Content = "Resubmit only", MinWidth = 120 };
        resubmitOnly.Click += (_, _) => Close(ResubmitAction.ResubmitOnly);

        var resubmitAndDelete = new Button
        {
            Content = "Resubmit and delete",
            MinWidth = 150,
            IsDefault = true,
            Foreground = new SolidColorBrush(Color.Parse("#C0392B"))
        };
        resubmitAndDelete.Click += (_, _) => Close(ResubmitAction.ResubmitAndDelete);

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
                new TextBlock
                {
                    Text = $"Send {messageCount:N0} message(s) back to “{targetName}”.",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "Resubmit only keeps the dead-lettered copies so you can retry again. " +
                           "Resubmit and delete removes them once the copies are sent.",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.65,
                    FontSize = 12
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, resubmitOnly, resubmitAndDelete }
                }
            }
        };
    }

    /// <summary>Returns the chosen action, or null when the user cancels.</summary>
    public static async Task<ResubmitAction?> ShowAsync(Window owner, int messageCount, string targetName)
    {
        var dialog = new ResubmitDialog(messageCount, targetName);
        return await dialog.ShowDialog<ResubmitAction?>(owner);
    }
}
