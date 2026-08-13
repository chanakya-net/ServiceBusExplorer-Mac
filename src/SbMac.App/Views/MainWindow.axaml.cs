using Avalonia.Controls;
// Avalonia 12 moved SetTextAsync off IClipboard onto ClipboardExtensions in this namespace.
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

using SbMac.App.Services;
using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Dialogs;
using SbMac.App.Views.Dialogs;

using SbMac.Core;
using SbMac.Core.Connections;
using SbMac.Core.ImportExport;

namespace SbMac.App.Views;

public partial class MainWindow : Window, IUiServices
{
    public MainWindow()
    {
        InitializeComponent();

        // The grid's SelectedItems isn't a bindable property in Avalonia's DataGrid, so
        // the multi-selection is mirrored into the view model by hand.
        MessageGrid.SelectionChanged += OnMessageSelectionChanged;
    }

    MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (ViewModel is { } viewModel)
        {
            viewModel.Ui = this;
        }
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (ViewModel is { } viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    void OnMessageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        viewModel.SelectedMessages.Clear();
        foreach (var item in MessageGrid.SelectedItems)
        {
            if (item is MessageRowViewModel row)
            {
                viewModel.SelectedMessages.Add(row);
            }
        }
    }

    // ------------------------------------------------------------ IUiServices

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool destructive = false) =>
        MessageDialog.ConfirmAsync(this, title, message, confirmText, destructive);

    public Task ShowErrorAsync(string title, string message) =>
        MessageDialog.ShowAsync(this, title, message, MessageDialogKind.Error);

    public Task ShowInfoAsync(string title, string message) =>
        MessageDialog.ShowAsync(this, title, message, MessageDialogKind.Info);

    public async Task<NamespaceConnection?> EditConnectionAsync(NamespaceConnection? existing)
    {
        var dialog = new ConnectionDialog { DataContext = new ConnectionDialogViewModel(existing) };
        return await dialog.ShowDialog<NamespaceConnection?>(this);
    }

    public async Task<SendMessageResult?> ComposeMessageAsync(SendMessageRequest request)
    {
        var dialog = new SendMessageDialog { DataContext = new SendMessageViewModel(request) };
        return await dialog.ShowDialog<SendMessageResult?>(this);
    }

    public async Task<QueueDefinition?> EditQueueAsync(QueueDefinition definition, bool isNew)
    {
        var dialog = new QueueEditorDialog { DataContext = new QueueEditorViewModel(definition, isNew) };
        return await dialog.ShowDialog<QueueDefinition?>(this);
    }

    public async Task<TopicDefinition?> EditTopicAsync(TopicDefinition definition, bool isNew)
    {
        var dialog = new TopicEditorDialog { DataContext = new TopicEditorViewModel(definition, isNew) };
        return await dialog.ShowDialog<TopicDefinition?>(this);
    }

    public async Task<SubscriptionDefinition?> EditSubscriptionAsync(
        string topicName,
        SubscriptionDefinition definition,
        bool isNew)
    {
        var dialog = new SubscriptionEditorDialog
        {
            DataContext = new SubscriptionEditorViewModel(topicName, definition, isNew)
        };

        return await dialog.ShowDialog<SubscriptionDefinition?>(this);
    }

    public async Task<RuleDefinition?> EditRuleAsync(RuleDefinition definition, bool isNew)
    {
        var dialog = new RuleEditorDialog { DataContext = new RuleEditorViewModel(definition, isNew) };
        return await dialog.ShowDialog<RuleDefinition?>(this);
    }

    public Task<ResubmitAction?> ChooseResubmitActionAsync(int messageCount, string targetName) =>
        ResubmitDialog.ShowAsync(this, messageCount, targetName);

    public async Task<ImportConflictPolicy?> ChooseImportPolicyAsync(NamespaceDefinition definition)
    {
        var subscriptionCount = definition.Topics.Sum(topic => topic.Subscriptions.Count);

        var summary =
            $"The file contains {definition.Queues.Count} queue(s), {definition.Topics.Count} topic(s) " +
            $"and {subscriptionCount} subscription(s).";

        if (!string.IsNullOrWhiteSpace(definition.SourceNamespace))
        {
            summary += $"\n\nExported from {definition.SourceNamespace}.";
        }

        return await ImportPolicyDialog.ShowAsync(this, summary);
    }

    public Task ShowImportResultAsync(ImportResult result)
    {
        var summary =
            $"{result.CreatedCount} created\n" +
            $"{result.UpdatedCount} updated\n" +
            $"{result.SkippedCount} skipped\n" +
            $"{result.FailedCount} failed";

        var failures = result.Steps
            .Where(step => !step.Succeeded)
            .Select(step => $"• {step.EntityPath}: {step.Error}")
            .ToList();

        if (failures.Count > 0)
        {
            summary += "\n\nFailures:\n" + string.Join("\n", failures.Take(20));

            if (failures.Count > 20)
            {
                summary += $"\n… and {failures.Count - 20} more.";
            }
        }

        return MessageDialog.ShowAsync(
            this,
            "Import finished",
            summary,
            result.FailedCount > 0 ? MessageDialogKind.Error : MessageDialogKind.Info);
    }

    public async Task<string?> PickOpenFileAsync(FilePickerRequest request)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = request.Title,
            AllowMultiple = false,
            FileTypeFilter = BuildFilters(request.Extensions)
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(FilePickerRequest request)
    {
        var startFolder = await StorageProvider.TryGetFolderFromPathAsync(AppPaths.DefaultExportDirectory);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = request.Title,
            SuggestedFileName = request.SuggestedFileName,
            SuggestedStartLocation = startFolder,
            DefaultExtension = request.Extensions.FirstOrDefault(),
            FileTypeChoices = BuildFilters(request.Extensions)
        });

        return file?.TryGetLocalPath();
    }

    public async Task SetClipboardTextAsync(string text)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    static List<FilePickerFileType> BuildFilters(string[] extensions)
    {
        var filters = extensions
            .Select(extension => new FilePickerFileType(extension.ToUpperInvariant())
            {
                Patterns = [$"*.{extension}"]
            })
            .ToList();

        filters.Add(new FilePickerFileType("All files") { Patterns = ["*"] });
        return filters;
    }
}
