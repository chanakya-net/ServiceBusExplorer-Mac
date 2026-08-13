using CommunityToolkit.Mvvm.ComponentModel;

using SbMac.Core.ImportExport;

namespace SbMac.App.ViewModels.Dialogs;

/// <summary>Backs the queue create/properties dialog.</summary>
public sealed partial class QueueEditorViewModel : ViewModelBase
{
    public QueueEditorViewModel(QueueDefinition definition, bool isNew)
    {
        IsNew = isNew;

        name = definition.Name;
        maxSizeInMegabytes = definition.MaxSizeInMegabytes ?? 1024;
        maxDeliveryCount = definition.MaxDeliveryCount;
        lockDuration = DurationText.ToDisplay(definition.LockDuration);
        defaultMessageTimeToLive = DurationText.ToDisplay(definition.DefaultMessageTimeToLive);
        autoDeleteOnIdle = DurationText.ToDisplay(definition.AutoDeleteOnIdle);
        duplicateDetectionHistoryTimeWindow = DurationText.ToDisplay(definition.DuplicateDetectionHistoryTimeWindow);
        requiresDuplicateDetection = definition.RequiresDuplicateDetection;
        requiresSession = definition.RequiresSession;
        enablePartitioning = definition.EnablePartitioning;
        deadLetteringOnMessageExpiration = definition.DeadLetteringOnMessageExpiration;
        enableBatchedOperations = definition.EnableBatchedOperations;
        isActive = !string.Equals(definition.Status, "Disabled", StringComparison.OrdinalIgnoreCase);
        forwardTo = definition.ForwardTo ?? string.Empty;
        forwardDeadLetteredMessagesTo = definition.ForwardDeadLetteredMessagesTo ?? string.Empty;
        userMetadata = definition.UserMetadata ?? string.Empty;
    }

    public bool IsNew { get; }

    public string DialogTitle => IsNew ? "Create queue" : $"Queue properties — {Name}";

    /// <summary>
    /// Partitioning, sessions and duplicate detection are fixed when the queue is created;
    /// Service Bus rejects an update that changes them, so the fields lock after creation.
    /// </summary>
    public bool CanEditCreateOnlySettings => IsNew;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    string name;

    [ObservableProperty]
    long maxSizeInMegabytes;

    [ObservableProperty]
    int maxDeliveryCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string lockDuration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string defaultMessageTimeToLive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string autoDeleteOnIdle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string duplicateDetectionHistoryTimeWindow;

    [ObservableProperty]
    bool requiresDuplicateDetection;

    [ObservableProperty]
    bool requiresSession;

    [ObservableProperty]
    bool enablePartitioning;

    [ObservableProperty]
    bool deadLetteringOnMessageExpiration;

    [ObservableProperty]
    bool enableBatchedOperations;

    [ObservableProperty]
    bool isActive;

    [ObservableProperty]
    string forwardTo;

    [ObservableProperty]
    string forwardDeadLetteredMessagesTo;

    [ObservableProperty]
    string userMetadata;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Name) &&
        DurationText.IsValid(LockDuration) &&
        DurationText.IsValid(DefaultMessageTimeToLive) &&
        DurationText.IsValid(AutoDeleteOnIdle) &&
        DurationText.IsValid(DuplicateDetectionHistoryTimeWindow);

    public QueueDefinition ToDefinition() => new()
    {
        Name = Name.Trim(),
        MaxSizeInMegabytes = MaxSizeInMegabytes,
        MaxDeliveryCount = MaxDeliveryCount,
        LockDuration = DurationText.ToDefinition(LockDuration),
        DefaultMessageTimeToLive = DurationText.ToDefinition(DefaultMessageTimeToLive),
        AutoDeleteOnIdle = DurationText.ToDefinition(AutoDeleteOnIdle),
        DuplicateDetectionHistoryTimeWindow = DurationText.ToDefinition(DuplicateDetectionHistoryTimeWindow),
        RequiresDuplicateDetection = RequiresDuplicateDetection,
        RequiresSession = RequiresSession,
        EnablePartitioning = EnablePartitioning,
        DeadLetteringOnMessageExpiration = DeadLetteringOnMessageExpiration,
        EnableBatchedOperations = EnableBatchedOperations,
        Status = IsActive ? "Active" : "Disabled",
        ForwardTo = NullIfBlank(ForwardTo),
        ForwardDeadLetteredMessagesTo = NullIfBlank(ForwardDeadLetteredMessagesTo),
        UserMetadata = NullIfBlank(UserMetadata)
    };

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Backs the topic create/properties dialog.</summary>
public sealed partial class TopicEditorViewModel : ViewModelBase
{
    public TopicEditorViewModel(TopicDefinition definition, bool isNew)
    {
        IsNew = isNew;

        name = definition.Name;
        maxSizeInMegabytes = definition.MaxSizeInMegabytes ?? 1024;
        defaultMessageTimeToLive = DurationText.ToDisplay(definition.DefaultMessageTimeToLive);
        autoDeleteOnIdle = DurationText.ToDisplay(definition.AutoDeleteOnIdle);
        duplicateDetectionHistoryTimeWindow = DurationText.ToDisplay(definition.DuplicateDetectionHistoryTimeWindow);
        requiresDuplicateDetection = definition.RequiresDuplicateDetection;
        enablePartitioning = definition.EnablePartitioning;
        supportOrdering = definition.SupportOrdering;
        enableBatchedOperations = definition.EnableBatchedOperations;
        isActive = !string.Equals(definition.Status, "Disabled", StringComparison.OrdinalIgnoreCase);
        userMetadata = definition.UserMetadata ?? string.Empty;
    }

    public bool IsNew { get; }

    public string DialogTitle => IsNew ? "Create topic" : $"Topic properties — {Name}";

    public bool CanEditCreateOnlySettings => IsNew;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    string name;

    [ObservableProperty]
    long maxSizeInMegabytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string defaultMessageTimeToLive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string autoDeleteOnIdle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string duplicateDetectionHistoryTimeWindow;

    [ObservableProperty]
    bool requiresDuplicateDetection;

    [ObservableProperty]
    bool enablePartitioning;

    [ObservableProperty]
    bool supportOrdering;

    [ObservableProperty]
    bool enableBatchedOperations;

    [ObservableProperty]
    bool isActive;

    [ObservableProperty]
    string userMetadata;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Name) &&
        DurationText.IsValid(DefaultMessageTimeToLive) &&
        DurationText.IsValid(AutoDeleteOnIdle) &&
        DurationText.IsValid(DuplicateDetectionHistoryTimeWindow);

    public TopicDefinition ToDefinition() => new()
    {
        Name = Name.Trim(),
        MaxSizeInMegabytes = MaxSizeInMegabytes,
        DefaultMessageTimeToLive = DurationText.ToDefinition(DefaultMessageTimeToLive),
        AutoDeleteOnIdle = DurationText.ToDefinition(AutoDeleteOnIdle),
        DuplicateDetectionHistoryTimeWindow = DurationText.ToDefinition(DuplicateDetectionHistoryTimeWindow),
        RequiresDuplicateDetection = RequiresDuplicateDetection,
        EnablePartitioning = EnablePartitioning,
        SupportOrdering = SupportOrdering,
        EnableBatchedOperations = EnableBatchedOperations,
        Status = IsActive ? "Active" : "Disabled",
        UserMetadata = NullIfBlank(UserMetadata)
    };

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Backs the subscription create/properties dialog.</summary>
public sealed partial class SubscriptionEditorViewModel : ViewModelBase
{
    public SubscriptionEditorViewModel(string topicName, SubscriptionDefinition definition, bool isNew)
    {
        TopicName = topicName;
        IsNew = isNew;

        name = definition.Name;
        maxDeliveryCount = definition.MaxDeliveryCount;
        lockDuration = DurationText.ToDisplay(definition.LockDuration);
        defaultMessageTimeToLive = DurationText.ToDisplay(definition.DefaultMessageTimeToLive);
        autoDeleteOnIdle = DurationText.ToDisplay(definition.AutoDeleteOnIdle);
        requiresSession = definition.RequiresSession;
        deadLetteringOnMessageExpiration = definition.DeadLetteringOnMessageExpiration;
        enableDeadLetteringOnFilterEvaluationExceptions = definition.EnableDeadLetteringOnFilterEvaluationExceptions;
        enableBatchedOperations = definition.EnableBatchedOperations;
        isActive = !string.Equals(definition.Status, "Disabled", StringComparison.OrdinalIgnoreCase);
        forwardTo = definition.ForwardTo ?? string.Empty;
        forwardDeadLetteredMessagesTo = definition.ForwardDeadLetteredMessagesTo ?? string.Empty;
        userMetadata = definition.UserMetadata ?? string.Empty;

        // A new subscription's default rule can only be set at creation time, so the
        // filter box is offered here and nowhere else.
        defaultRuleFilter = isNew ? "1=1" : string.Empty;
    }

    public string TopicName { get; }

    public bool IsNew { get; }

    public string DialogTitle => IsNew
        ? $"Create subscription on {TopicName}"
        : $"Subscription properties — {TopicName}/{Name}";

    public bool CanEditCreateOnlySettings => IsNew;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    string name;

    [ObservableProperty]
    int maxDeliveryCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string lockDuration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string defaultMessageTimeToLive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string autoDeleteOnIdle;

    [ObservableProperty]
    bool requiresSession;

    [ObservableProperty]
    bool deadLetteringOnMessageExpiration;

    [ObservableProperty]
    bool enableDeadLetteringOnFilterEvaluationExceptions;

    [ObservableProperty]
    bool enableBatchedOperations;

    [ObservableProperty]
    bool isActive;

    [ObservableProperty]
    string forwardTo;

    [ObservableProperty]
    string forwardDeadLetteredMessagesTo;

    [ObservableProperty]
    string userMetadata;

    [ObservableProperty]
    string defaultRuleFilter;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Name) &&
        DurationText.IsValid(LockDuration) &&
        DurationText.IsValid(DefaultMessageTimeToLive) &&
        DurationText.IsValid(AutoDeleteOnIdle);

    public SubscriptionDefinition ToDefinition()
    {
        var definition = new SubscriptionDefinition
        {
            Name = Name.Trim(),
            MaxDeliveryCount = MaxDeliveryCount,
            LockDuration = DurationText.ToDefinition(LockDuration),
            DefaultMessageTimeToLive = DurationText.ToDefinition(DefaultMessageTimeToLive),
            AutoDeleteOnIdle = DurationText.ToDefinition(AutoDeleteOnIdle),
            RequiresSession = RequiresSession,
            DeadLetteringOnMessageExpiration = DeadLetteringOnMessageExpiration,
            EnableDeadLetteringOnFilterEvaluationExceptions = EnableDeadLetteringOnFilterEvaluationExceptions,
            EnableBatchedOperations = EnableBatchedOperations,
            Status = IsActive ? "Active" : "Disabled",
            ForwardTo = NullIfBlank(ForwardTo),
            ForwardDeadLetteredMessagesTo = NullIfBlank(ForwardDeadLetteredMessagesTo),
            UserMetadata = NullIfBlank(UserMetadata)
        };

        if (IsNew && !string.IsNullOrWhiteSpace(DefaultRuleFilter))
        {
            definition.Rules.Add(new RuleDefinition
            {
                Name = "$Default",
                FilterType = "Sql",
                SqlExpression = DefaultRuleFilter.Trim()
            });
        }

        return definition;
    }

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Backs the rule create/edit dialog.</summary>
public sealed partial class RuleEditorViewModel : ViewModelBase
{
    public RuleEditorViewModel(RuleDefinition definition, bool isNew)
    {
        IsNew = isNew;

        name = definition.Name;
        sqlExpression = definition.SqlExpression ?? "1=1";
        sqlAction = definition.SqlAction ?? string.Empty;
        correlationId = definition.CorrelationId ?? string.Empty;
        messageId = definition.MessageId ?? string.Empty;
        subject = definition.Subject ?? string.Empty;
        to = definition.To ?? string.Empty;
        replyTo = definition.ReplyTo ?? string.Empty;
        sessionId = definition.SessionId ?? string.Empty;
        contentType = definition.ContentType ?? string.Empty;

        selectedFilterIndex = definition.FilterType.ToLowerInvariant() switch
        {
            "correlation" => 1,
            "true" => 2,
            "false" => 3,
            _ => 0
        };
    }

    public bool IsNew { get; }

    public string DialogTitle => IsNew ? "Create rule" : $"Rule — {Name}";

    public IReadOnlyList<string> FilterTypes { get; } = ["SQL filter", "Correlation filter", "True (match all)", "False (match none)"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    string name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(ShowSqlFields))]
    [NotifyPropertyChangedFor(nameof(ShowCorrelationFields))]
    int selectedFilterIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    string sqlExpression;

    [ObservableProperty]
    string sqlAction;

    [ObservableProperty]
    string correlationId;

    [ObservableProperty]
    string messageId;

    [ObservableProperty]
    string subject;

    [ObservableProperty]
    string to;

    [ObservableProperty]
    string replyTo;

    [ObservableProperty]
    string sessionId;

    [ObservableProperty]
    string contentType;

    public bool ShowSqlFields => SelectedFilterIndex == 0;

    public bool ShowCorrelationFields => SelectedFilterIndex == 1;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Name) &&
        (SelectedFilterIndex != 0 || !string.IsNullOrWhiteSpace(SqlExpression));

    public RuleDefinition ToDefinition() => new()
    {
        Name = Name.Trim(),
        FilterType = SelectedFilterIndex switch
        {
            1 => "Correlation",
            2 => "True",
            3 => "False",
            _ => "Sql"
        },
        SqlExpression = SqlExpression,
        SqlAction = NullIfBlank(SqlAction),
        CorrelationId = NullIfBlank(CorrelationId),
        MessageId = NullIfBlank(MessageId),
        Subject = NullIfBlank(Subject),
        To = NullIfBlank(To),
        ReplyTo = NullIfBlank(ReplyTo),
        SessionId = NullIfBlank(SessionId),
        ContentType = NullIfBlank(ContentType)
    };

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
