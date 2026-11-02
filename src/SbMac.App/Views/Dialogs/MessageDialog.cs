using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SbMac.App.Views.Dialogs;

public enum MessageDialogKind
{
    Info,
    Error
}

/// <summary>
/// Alert and confirmation sheets. Built in code rather than XAML because the layout is
/// three controls and every caller wants a slightly different button set.
/// </summary>
public sealed class MessageDialog : Window
{
    MessageDialog(
        string title,
        string message,
        MessageDialogKind kind,
        string? confirmText,
        bool destructive,
        string dismissText)
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MinHeight = 150;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        // Style classes rather than fixed brushes, so an open alert follows a theme change.
        if (kind == MessageDialogKind.Error)
        {
            heading.Classes.Add("error");
        }

        var body = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        // A confirmation gets Cancel + the action; a plain alert only needs a dismiss button.
        if (confirmText is not null)
        {
            var cancel = new Button { Content = dismissText, MinWidth = 88, IsCancel = true };
            cancel.Classes.Add("secondary");
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);

            var confirm = new Button { Content = confirmText, MinWidth = 88, IsDefault = true };
            if (destructive)
            {
                confirm.Classes.Add("destructive");
            }

            if (!destructive) confirm.Classes.Add("primary");
            confirm.Click += (_, _) => Close(true);
            buttons.Children.Add(confirm);
        }
        else
        {
            var ok = new Button { Content = "OK", MinWidth = 88, IsDefault = true, IsCancel = true };
            ok.Classes.Add("primary");
            ok.Click += (_, _) => Close(true);
            buttons.Children.Add(ok);
        }

        var scroller = new ScrollViewer
        {
            Content = body,
            MaxHeight = 340,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        Opened += (_, _) =>
        {
            ((Button)buttons.Children[^1]).Focus(Avalonia.Input.NavigationMethod.Tab);
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen is null) return;
            var frame = FrameSize ?? ClientSize;
            var available = screen.WorkingArea.Height / screen.Scaling - (frame.Height - ClientSize.Height);
            scroller.MaxHeight = Math.Max(0, Math.Min(340, available - heading.Bounds.Height - buttons.Bounds.Height - 68));
            MaxHeight = Math.Max(0, available);
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 14,
            Children =
            {
                heading,
                scroller,
                buttons
            }
        };
    }

    public static async Task<bool> ConfirmAsync(
        Window owner,
        string title,
        string message,
        string confirmText,
        bool destructive,
        string dismissText = "Cancel")
    {
        var dialog = new MessageDialog(
            title, message, MessageDialogKind.Info, confirmText, destructive, dismissText);
        return await dialog.ShowDialog<bool>(owner);
    }

    public static async Task ShowAsync(Window owner, string title, string message, MessageDialogKind kind)
    {
        var dialog = new MessageDialog(
            title, message, kind, confirmText: null, destructive: false, dismissText: "OK");
        await dialog.ShowDialog<bool>(owner);
    }
}
