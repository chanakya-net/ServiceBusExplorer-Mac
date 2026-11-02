using System.Xml;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;

using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace SbMac.App.Views.Controls;

/// <summary>
/// Read-only view of a decoded message body. JSON and XML are highlighted with local,
/// theme-specific definitions; every other format shows as plain text.
/// </summary>
public partial class MessageBodyViewer : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MessageBodyViewer, string?>(nameof(Text));

    /// <summary>The decoder's format label (e.g. "JSON", "XML", "Binary (hex)").</summary>
    public static readonly StyledProperty<string?> FormatProperty =
        AvaloniaProperty.Register<MessageBodyViewer, string?>(nameof(Format));

    static readonly Dictionary<string, IHighlightingDefinition> Definitions = [];

    bool themeSubscribed;
    readonly List<int> matches = [];
    int matchIndex = -1;

    public MessageBodyViewer()
    {
        InitializeComponent();
        Avalonia.Automation.AutomationProperties.SetName(Editor.TextArea, "Message body text");
        WrapToggle.IsCheckedChanged += (_, _) => Editor.WordWrap = WrapToggle.IsChecked == true;
        OpenFindButton.Click += (_, _) => OpenFind();
        FindQuery.TextChanged += (_, _) => RefreshMatches();
        PreviousButton.Click += (_, _) => FindPrevious();
        NextButton.Click += (_, _) => FindNext();
        CloseFindButton.Click += (_, _) => CloseFind();
        AddHandler(KeyDownEvent, HandleFindKey, RoutingStrategies.Tunnel);

    }

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public string? Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    /// <summary>The currently selected text, or empty when nothing is selected.</summary>
    public string SelectedText => Editor.SelectedText ?? string.Empty;

    internal bool IsThemeSubscribed => themeSubscribed;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!themeSubscribed)
        {
            ActualThemeVariantChanged += OnThemeChanged;
            themeSubscribed = true;
        }

        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (themeSubscribed)
        {
            ActualThemeVariantChanged -= OnThemeChanged;
            themeSubscribed = false;
        }

        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FormatProperty)
        {
            Refresh();
        }
    }

    void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    void Refresh()
    {
        var editor = Editor;
        var text = Text ?? string.Empty;
        if (!string.Equals(editor.Text, text, StringComparison.Ordinal))
        {
            editor.Text = text;
            RefreshMatches();
        }

        editor.SyntaxHighlighting = DefinitionFor(Format, ActualThemeVariant);
    }

    internal void OpenFind()
    {
        FindBar.IsVisible = true;
        FindQuery.Focus(NavigationMethod.Tab);
        FindQuery.SelectAll();
        RefreshMatches();
    }

    void CloseFind()
    {
        FindBar.IsVisible = false;
        FindQuery.Text = string.Empty;
        Editor.Select(0, 0);
        Editor.TextArea.Focus(NavigationMethod.Tab);
    }

    void HandleFindKey(object? sender, KeyEventArgs e)
    {
        if (!IsKeyboardFocusWithin) return;
        if (e.Key == Key.Tab && Editor.IsKeyboardFocusWithin &&
            (e.KeyModifiers == KeyModifiers.None || e.KeyModifiers == KeyModifiers.Shift))
        {
            if (e.KeyModifiers == KeyModifiers.Shift)
                (FindBar.IsVisible ? CloseFindButton : OpenFindButton).Focus(NavigationMethod.Tab);
            else
                TopLevel.GetTopLevel(this)?.FocusManager?.TryMoveFocus(NavigationDirection.Next);
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Meta)
        {
            OpenFind();
            e.Handled = true;
        }
        else if (FindBar.IsVisible && e.Key == Key.Escape)
        {
            CloseFind();
            e.Handled = true;
        }
        else if (FindBar.IsVisible && e.Key == Key.Enter && FindQuery.IsKeyboardFocusWithin)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) FindPrevious(); else FindNext();
            e.Handled = true;
        }
    }

    void RefreshMatches()
    {
        matches.Clear();
        matchIndex = -1;
        var query = FindQuery.Text ?? string.Empty;
        var text = Editor.Text ?? string.Empty;
        if (query.Length > 0)
        {
            for (var start = 0; start <= text.Length - query.Length;)
            {
                var offset = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
                if (offset < 0) break;
                matches.Add(offset);
                start = offset + query.Length;
            }
        }
        if (matches.Count > 0) matchIndex = 0;
        ShowMatch();
    }

    internal void FindNext() => Navigate(1);
    internal void FindPrevious() => Navigate(-1);

    void Navigate(int direction)
    {
        if (matches.Count == 0) return;
        matchIndex = (matchIndex + direction + matches.Count) % matches.Count;
        ShowMatch();
    }

    void ShowMatch()
    {
        if (matchIndex < 0)
        {
            Editor.Select(0, 0);
            FindStatus.Text = string.IsNullOrEmpty(FindQuery.Text) ? "" : "No matches";
            return;
        }
        var offset = matches[matchIndex];
        Editor.Select(offset, FindQuery.Text!.Length);
        var location = Editor.Document.GetLocation(offset);
        Editor.ScrollTo(location.Line, location.Column);
        FindStatus.Text = $"{matchIndex + 1} of {matches.Count}";
    }

    static IHighlightingDefinition? DefinitionFor(string? format, ThemeVariant theme)
    {
        var language = format switch { "JSON" => "Json", "XML" => "Xml", _ => null };
        if (language is null)
        {
            return null;
        }

        var variant = theme == ThemeVariant.Dark ? "Dark" : "Light";
        var name = $"{language}.{variant}";
        if (!Definitions.TryGetValue(name, out var definition))
        {
            definition = Load(name);
            Definitions[name] = definition;
        }

        return definition;
    }

    static IHighlightingDefinition Load(string name)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://SbMac/Styles/Syntax/{name}.xshd"));
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
