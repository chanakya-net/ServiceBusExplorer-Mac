using SbMac.App.ViewModels.Dialogs;
using SbMac.Core.Connections;
using SbMac.Core.ImportExport;

namespace SbMac.App.Services;

/// <summary>Options for a file picker.</summary>
public sealed record FilePickerRequest(string Title, string? SuggestedFileName = null, params string[] Extensions);

/// <summary>What to do with the dead-letter copies after a resubmit.</summary>
public enum ResubmitAction
{
    /// <summary>Send copies back and leave the dead-lettered originals in place.</summary>
    ResubmitOnly,

    /// <summary>Send copies back, then delete the originals from the dead-letter queue.</summary>
    ResubmitAndDelete
}

/// <summary>
/// Everything the main view model needs from the window layer: dialogs, file pickers
/// and the clipboard. Keeping it behind an interface means the view models hold no
/// reference to a <c>Window</c>.
/// </summary>
public interface IUiServices
{
    Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool destructive = false);

    Task ShowErrorAsync(string title, string message);

    Task ShowInfoAsync(string title, string message);

    Task ShowUpdateAvailableAsync(UpdateInfo update);

    /// <summary>Add or edit a saved namespace. Returns null when the user cancels.</summary>
    Task<NamespaceConnection?> EditConnectionAsync(NamespaceConnection? existing);

    /// <summary>Compose one or more messages to send. Returns null when the user cancels.</summary>
    Task<SendMessageResult?> ComposeMessageAsync(SendMessageRequest request);

    /// <summary>Create or edit a queue definition. Returns null when the user cancels.</summary>
    Task<QueueDefinition?> EditQueueAsync(QueueDefinition definition, bool isNew);

    /// <summary>Create or edit a topic definition. Returns null when the user cancels.</summary>
    Task<TopicDefinition?> EditTopicAsync(TopicDefinition definition, bool isNew);

    /// <summary>Create or edit a subscription definition. Returns null when the user cancels.</summary>
    Task<SubscriptionDefinition?> EditSubscriptionAsync(string topicName, SubscriptionDefinition definition, bool isNew);

    /// <summary>Create or edit a subscription rule. Returns null when the user cancels.</summary>
    Task<RuleDefinition?> EditRuleAsync(RuleDefinition definition, bool isNew);

    /// <summary>
    /// Ask whether a resubmit should also delete the dead-letter originals. Returns null
    /// when the user cancels, which must abort the resubmit entirely.
    /// </summary>
    Task<ResubmitAction?> ChooseResubmitActionAsync(int messageCount, string targetName);

    /// <summary>Choose how an import should handle entities that already exist.</summary>
    Task<ImportConflictPolicy?> ChooseImportPolicyAsync(NamespaceDefinition definition);

    /// <summary>Show the result of an import run.</summary>
    Task ShowImportResultAsync(ImportResult result);

    Task<string?> PickOpenFileAsync(FilePickerRequest request);

    Task<string?> PickSaveFileAsync(FilePickerRequest request);

    Task SetClipboardTextAsync(string text);
}
