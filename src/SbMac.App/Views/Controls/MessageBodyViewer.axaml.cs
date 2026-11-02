using System.Xml;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
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

    public MessageBodyViewer()
    {
        InitializeComponent();
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
        }

        editor.SyntaxHighlighting = DefinitionFor(Format, ActualThemeVariant);
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
