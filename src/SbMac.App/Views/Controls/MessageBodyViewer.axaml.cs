using System.Text;
using System.Xml;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

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
    /// <summary>Displayed UTF-8 size above which syntax highlighting is switched off.</summary>
    internal const int HighlightByteLimit = 1024 * 1024;
    /// <summary>Bodies longer than this load on a later dispatcher turn so rapid selection supersedes them.</summary>
    internal const int DeferredLoadChars = 64 * 1024;
    internal const int MaxMatches = 10_000;
    /// <summary>A single line longer than this is not soft-wrapped: wrapping one huge line costs minutes of layout.</summary>
    internal const int WrapLineLimit = 200_000;

    const string PlainTextNotice = "This body is larger than 1 MiB, so syntax highlighting is off and it is shown as plain text.";
    const string NoWrapNotice = "A line in this body is extremely long, so word wrap is not applied to it.";
    const string FullTextNotice = "The full text is still here: scroll, search, select or copy it.";
    bool hasHugeLine;

    List<int> matches = [];
    bool matchesCapped;
    int matchIndex = -1;
    int loadVersion;
    CancellationTokenSource? searchCts;
    bool searchInvalidated;

    /// <summary>Number of times the editor document was actually replaced (test/diagnostic aid).</summary>
    internal int DocumentLoads { get; private set; }
    internal bool IsSearching { get; private set; }

    public MessageBodyViewer()
    {
        InitializeComponent();
        Avalonia.Automation.AutomationProperties.SetName(Editor.TextArea, "Message body text");
        WrapToggle.IsCheckedChanged += (_, _) => ApplyWrap();
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
        var text = Text ?? string.Empty;
        var version = ++loadVersion;
        if (text.Length > DeferredLoadChars && !string.Equals(Editor.Text, text, StringComparison.Ordinal))
        {
            searchCts?.Cancel();
            searchInvalidated = true;
            IsSearching = false;
            Dispatcher.UIThread.Post(() => { if (version == loadVersion) Apply(text); }, DispatcherPriority.Background);
            return;
        }

        Apply(text);
    }

    void Apply(string text)
    {
        var editor = Editor;
        var plainOnly = ExceedsHighlightLimit(text);
        hasHugeLine = HasLineLongerThan(text, WrapLineLimit);
        HighlightNotice.Text = string.Join(" ", new[] { plainOnly ? PlainTextNotice : null, hasHugeLine ? NoWrapNotice : null, FullTextNotice }.OfType<string>());
        HighlightNotice.IsVisible = plainOnly || hasHugeLine;
        ApplyWrap();
        editor.SyntaxHighlighting = plainOnly ? null : DefinitionFor(Format, ActualThemeVariant);
        if (string.Equals(editor.Text, text, StringComparison.Ordinal))
        {
            if (searchInvalidated) RefreshMatches();
            return;
        }
        editor.Text = text;
        DocumentLoads++;
        RefreshMatches();
    }

    void ApplyWrap() => Editor.WordWrap = WrapToggle.IsChecked == true && !hasHugeLine;

    internal static bool HasLineLongerThan(string text, int limit)
    {
        if (text.Length <= limit) return false;
        var length = 0;
        foreach (var character in text)
        {
            if (character is '\r' or '\n') length = 0;
            else if (++length > limit) return true;
        }

        return false;
    }

    static bool ExceedsHighlightLimit(string text) =>
        text.Length > HighlightByteLimit
        || (text.Length * 3L > HighlightByteLimit && Encoding.UTF8.GetByteCount(text) > HighlightByteLimit);

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
        searchInvalidated = false;
        searchCts?.Cancel();
        searchCts = null;
        IsSearching = false;
        matches = [];
        matchesCapped = false;
        matchIndex = -1;
        var query = FindQuery.Text ?? string.Empty;
        var text = Editor.Text ?? string.Empty;
        if (query.Length == 0 || text.Length <= DeferredLoadChars)
        {
            (matches, matchesCapped) = Scan(text, query, CancellationToken.None);
            FinishSearch();
            return;
        }

        StartBackgroundSearch(text, query);
    }

    void StartBackgroundSearch(string text, string query)
    {
        var cts = searchCts = new CancellationTokenSource();
        IsSearching = true;
        FindStatus.Text = "Searching…";
        Editor.Select(0, 0);
        _ = Task.Run(() => Scan(text, query, cts.Token), cts.Token).ContinueWith(task =>
        {
            if (cts.IsCancellationRequested || !task.IsCompletedSuccessfully) return;
            (matches, matchesCapped) = task.Result;
            IsSearching = false;
            FinishSearch();
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    void FinishSearch()
    {
        if (matches.Count > 0) matchIndex = 0;
        ShowMatch();
    }

    static (List<int> Matches, bool Capped) Scan(string text, string query, CancellationToken token)
    {
        var found = new List<int>();
        if (query.Length == 0) return (found, false);
        for (var start = 0; start <= text.Length - query.Length;)
        {
            token.ThrowIfCancellationRequested();
            var offset = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (offset < 0) break;
            if (found.Count == MaxMatches) return (found, true);
            found.Add(offset);
            start = offset + query.Length;
        }

        return (found, false);
    }

    internal void FindNext() => Navigate(1);
    internal void FindPrevious() => Navigate(-1);

    void Navigate(int direction)
    {
        if (matches.Count == 0 || IsSearching) return;
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
        FindStatus.Text = $"{matchIndex + 1} of {matches.Count}{(matchesCapped ? "+" : "")}";
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
