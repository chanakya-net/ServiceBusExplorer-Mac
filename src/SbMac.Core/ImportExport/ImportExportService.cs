using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Serialization;

using Azure;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

using SbMac.Core.Entities;

namespace SbMac.Core.ImportExport;

/// <summary>File formats an entity definition set can be written to.</summary>
public enum DefinitionFormat
{
    Xml,
    Json
}

/// <summary>What to do when an entity being imported already exists.</summary>
public enum ImportConflictPolicy
{
    /// <summary>Leave the existing entity untouched.</summary>
    Skip,

    /// <summary>Apply the imported settings to the existing entity.</summary>
    Update,

    /// <summary>Stop the import and report the conflict.</summary>
    Fail
}

/// <summary>One line of the import log, so the UI can show exactly what happened.</summary>
public readonly record struct ImportStep(string EntityPath, string Action, string? Error = null)
{
    public bool Succeeded => Error is null;
}

/// <summary>Outcome of an import run.</summary>
public sealed record ImportResult(IReadOnlyList<ImportStep> Steps)
{
    public int CreatedCount => Steps.Count(step => step is { Succeeded: true, Action: "created" });
    public int UpdatedCount => Steps.Count(step => step is { Succeeded: true, Action: "updated" });
    public int SkippedCount => Steps.Count(step => step is { Succeeded: true, Action: "skipped" });
    public int FailedCount => Steps.Count(step => !step.Succeeded);
}

/// <summary>
/// Saves entity definitions to a file and recreates them from one — the cross-namespace
/// copy and backup path from the Windows tool.
/// </summary>
public sealed class ImportExportService
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    readonly EntityService entities;

    public ImportExportService(EntityService entities)
    {
        this.entities = entities;
    }

    // ---------------------------------------------------------------- export

    /// <summary>
    /// Reads the whole namespace into a definition set. Pass name filters to export a subset.
    /// </summary>
    public async Task<NamespaceDefinition> ExportAsync(
        string? sourceNamespace = null,
        IReadOnlySet<string>? queueNames = null,
        IReadOnlySet<string>? topicNames = null,
        CancellationToken cancellationToken = default)
    {
        var definition = new NamespaceDefinition
        {
            SourceNamespace = sourceNamespace,
            ExportedAt = DateTimeOffset.UtcNow.ToString("O")
        };

        foreach (var queue in await entities.GetQueuesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (queueNames is not null && !queueNames.Contains(queue.Name))
            {
                continue;
            }

            definition.Queues.Add(ToDefinition(queue.Properties));
        }

        foreach (var topic in await entities.GetTopicsAsync(cancellationToken).ConfigureAwait(false))
        {
            if (topicNames is not null && !topicNames.Contains(topic.Name))
            {
                continue;
            }

            var topicDefinition = ToDefinition(topic.Properties);

            foreach (var subscription in await entities
                .GetSubscriptionsAsync(topic.Name, cancellationToken).ConfigureAwait(false))
            {
                var subscriptionDefinition = ToDefinition(subscription.Properties);

                foreach (var rule in await entities
                    .GetRulesAsync(topic.Name, subscription.Name, cancellationToken).ConfigureAwait(false))
                {
                    subscriptionDefinition.Rules.Add(ToDefinition(rule));
                }

                topicDefinition.Subscriptions.Add(subscriptionDefinition);
            }

            definition.Topics.Add(topicDefinition);
        }

        return definition;
    }

    public async Task WriteAsync(
        NamespaceDefinition definition,
        string filePath,
        DefinitionFormat format,
        CancellationToken cancellationToken = default)
    {
        await File.WriteAllTextAsync(filePath, Serialize(definition, format), Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);
    }

    public static string Serialize(NamespaceDefinition definition, DefinitionFormat format)
    {
        if (format == DefinitionFormat.Json)
        {
            return JsonSerializer.Serialize(definition, JsonOptions);
        }

        var serializer = new XmlSerializer(typeof(NamespaceDefinition));
        var builder = new StringBuilder();

        using var writer = XmlWriter.Create(builder, new XmlWriterSettings
        {
            Indent = true,
            Encoding = Encoding.UTF8
        });

        serializer.Serialize(writer, definition);
        return builder.ToString();
    }

    // ---------------------------------------------------------------- import

    /// <summary>Reads a definition file, picking the format from its extension.</summary>
    public static async Task<NamespaceDefinition> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
        var format = Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? DefinitionFormat.Json
            : DefinitionFormat.Xml;

        return Deserialize(content, format);
    }

    public static NamespaceDefinition Deserialize(string content, DefinitionFormat format)
    {
        if (format == DefinitionFormat.Json)
        {
            return JsonSerializer.Deserialize<NamespaceDefinition>(content, JsonOptions)
                ?? throw new InvalidDataException("The file did not contain a namespace definition.");
        }

        var serializer = new XmlSerializer(typeof(NamespaceDefinition));
        using var reader = new StringReader(content);
        return serializer.Deserialize(reader) as NamespaceDefinition
            ?? throw new InvalidDataException("The file did not contain a namespace definition.");
    }

    /// <summary>
    /// Recreates the definitions in the connected namespace.
    /// </summary>
    /// <remarks>
    /// Each entity is applied independently: one failure is recorded and the run
    /// continues, so a single bad definition doesn't abandon the rest half-imported.
    /// The exception is <see cref="ImportConflictPolicy.Fail"/>, which is the caller
    /// explicitly asking to stop on the first conflict.
    /// </remarks>
    public async Task<ImportResult> ImportAsync(
        NamespaceDefinition definition,
        ImportConflictPolicy conflictPolicy = ImportConflictPolicy.Skip,
        IProgress<ImportStep>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<ImportStep>();

        void Record(ImportStep step)
        {
            steps.Add(step);
            progress?.Report(step);
        }

        foreach (var queue in definition.Queues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Record(await ImportQueueAsync(queue, conflictPolicy, cancellationToken).ConfigureAwait(false));
        }

        foreach (var topic in definition.Topics)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var topicStep = await ImportTopicAsync(topic, conflictPolicy, cancellationToken).ConfigureAwait(false);
            Record(topicStep);

            // Subscriptions can't be created under a topic that doesn't exist, so skip
            // the subtree rather than emitting a failure per subscription.
            if (!topicStep.Succeeded)
            {
                continue;
            }

            foreach (var subscription in topic.Subscriptions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var subscriptionStep = await ImportSubscriptionAsync(
                    topic.Name, subscription, conflictPolicy, cancellationToken).ConfigureAwait(false);
                Record(subscriptionStep);

                if (!subscriptionStep.Succeeded)
                {
                    continue;
                }

                foreach (var rule in subscription.Rules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Record(await ImportRuleAsync(
                        topic.Name, subscription.Name, rule, conflictPolicy, cancellationToken).ConfigureAwait(false));
                }
            }
        }

        return new ImportResult(steps);
    }

    async Task<ImportStep> ImportQueueAsync(
        QueueDefinition definition,
        ImportConflictPolicy conflictPolicy,
        CancellationToken cancellationToken)
    {
        var path = definition.Name;

        try
        {
            if (await entities.QueueExistsAsync(definition.Name, cancellationToken).ConfigureAwait(false))
            {
                switch (conflictPolicy)
                {
                    case ImportConflictPolicy.Skip:
                        return new ImportStep(path, "skipped");

                    case ImportConflictPolicy.Fail:
                        return new ImportStep(path, "failed", "A queue with this name already exists.");

                    case ImportConflictPolicy.Update:
                        var existing = (await entities.GetQueueAsync(definition.Name, cancellationToken)
                            .ConfigureAwait(false)).Properties;
                        ApplyUpdatable(definition, existing);
                        await entities.UpdateQueueAsync(existing, cancellationToken).ConfigureAwait(false);
                        return new ImportStep(path, "updated");
                }
            }

            await entities.CreateQueueAsync(ToCreateOptions(definition), cancellationToken).ConfigureAwait(false);
            return new ImportStep(path, "created");
        }
        catch (Exception exception) when (exception is RequestFailedException or ServiceBusException)
        {
            return new ImportStep(path, "failed", exception.Message);
        }
    }

    async Task<ImportStep> ImportTopicAsync(
        TopicDefinition definition,
        ImportConflictPolicy conflictPolicy,
        CancellationToken cancellationToken)
    {
        var path = definition.Name;

        try
        {
            if (await entities.TopicExistsAsync(definition.Name, cancellationToken).ConfigureAwait(false))
            {
                switch (conflictPolicy)
                {
                    case ImportConflictPolicy.Skip:
                        // The topic staying as-is is fine; its subscriptions still get imported.
                        return new ImportStep(path, "skipped");

                    case ImportConflictPolicy.Fail:
                        return new ImportStep(path, "failed", "A topic with this name already exists.");

                    case ImportConflictPolicy.Update:
                        var existing = (await entities.GetTopicAsync(definition.Name, cancellationToken)
                            .ConfigureAwait(false)).Properties;
                        ApplyUpdatable(definition, existing);
                        await entities.UpdateTopicAsync(existing, cancellationToken).ConfigureAwait(false);
                        return new ImportStep(path, "updated");
                }
            }

            await entities.CreateTopicAsync(ToCreateOptions(definition), cancellationToken).ConfigureAwait(false);
            return new ImportStep(path, "created");
        }
        catch (Exception exception) when (exception is RequestFailedException or ServiceBusException)
        {
            return new ImportStep(path, "failed", exception.Message);
        }
    }

    async Task<ImportStep> ImportSubscriptionAsync(
        string topicName,
        SubscriptionDefinition definition,
        ImportConflictPolicy conflictPolicy,
        CancellationToken cancellationToken)
    {
        var path = $"{topicName}/Subscriptions/{definition.Name}";

        try
        {
            SubscriptionProperties? existing = null;
            try
            {
                existing = (await entities
                    .GetSubscriptionAsync(topicName, definition.Name, cancellationToken)
                    .ConfigureAwait(false)).Properties;
            }
            catch (ServiceBusException notFound)
                when (notFound.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
            {
                // Expected: the subscription doesn't exist yet, which is the create path.
            }

            if (existing is not null)
            {
                switch (conflictPolicy)
                {
                    case ImportConflictPolicy.Skip:
                        return new ImportStep(path, "skipped");

                    case ImportConflictPolicy.Fail:
                        return new ImportStep(path, "failed", "A subscription with this name already exists.");

                    case ImportConflictPolicy.Update:
                        ApplyUpdatable(definition, existing);
                        await entities.UpdateSubscriptionAsync(existing, cancellationToken).ConfigureAwait(false);
                        return new ImportStep(path, "updated");
                }
            }

            await entities
                .CreateSubscriptionAsync(ToCreateOptions(topicName, definition), cancellationToken)
                .ConfigureAwait(false);
            return new ImportStep(path, "created");
        }
        catch (Exception exception) when (exception is RequestFailedException or ServiceBusException)
        {
            return new ImportStep(path, "failed", exception.Message);
        }
    }

    async Task<ImportStep> ImportRuleAsync(
        string topicName,
        string subscriptionName,
        RuleDefinition definition,
        ImportConflictPolicy conflictPolicy,
        CancellationToken cancellationToken)
    {
        var path = $"{topicName}/Subscriptions/{subscriptionName}/Rules/{definition.Name}";

        try
        {
            var existing = (await entities
                    .GetRulesAsync(topicName, subscriptionName, cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(rule => rule.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                switch (conflictPolicy)
                {
                    case ImportConflictPolicy.Skip:
                        return new ImportStep(path, "skipped");

                    case ImportConflictPolicy.Fail:
                        return new ImportStep(path, "failed", "A rule with this name already exists.");

                    case ImportConflictPolicy.Update:
                        var replacement = ToCreateOptions(definition);
                        existing.Filter = replacement.Filter;
                        existing.Action = replacement.Action;
                        await entities
                            .UpdateRuleAsync(topicName, subscriptionName, existing, cancellationToken)
                            .ConfigureAwait(false);
                        return new ImportStep(path, "updated");
                }
            }

            await entities
                .CreateRuleAsync(topicName, subscriptionName, ToCreateOptions(definition), cancellationToken)
                .ConfigureAwait(false);
            return new ImportStep(path, "created");
        }
        catch (Exception exception) when (exception is RequestFailedException or ServiceBusException)
        {
            return new ImportStep(path, "failed", exception.Message);
        }
    }

    // ------------------------------------------------------- SDK ⇄ DTO mapping

    public static QueueDefinition ToDefinition(QueueProperties properties) => new()
    {
        Name = properties.Name,
        LockDuration = FormatDuration(properties.LockDuration),
        MaxSizeInMegabytes = properties.MaxSizeInMegabytes,
        MaxMessageSizeInKilobytes = properties.MaxMessageSizeInKilobytes,
        RequiresDuplicateDetection = properties.RequiresDuplicateDetection,
        RequiresSession = properties.RequiresSession,
        DefaultMessageTimeToLive = FormatDuration(properties.DefaultMessageTimeToLive),
        AutoDeleteOnIdle = FormatDuration(properties.AutoDeleteOnIdle),
        DeadLetteringOnMessageExpiration = properties.DeadLetteringOnMessageExpiration,
        DuplicateDetectionHistoryTimeWindow = FormatDuration(properties.DuplicateDetectionHistoryTimeWindow),
        MaxDeliveryCount = properties.MaxDeliveryCount,
        EnableBatchedOperations = properties.EnableBatchedOperations,
        EnablePartitioning = properties.EnablePartitioning,
        Status = properties.Status.ToString(),
        ForwardTo = properties.ForwardTo,
        ForwardDeadLetteredMessagesTo = properties.ForwardDeadLetteredMessagesTo,
        UserMetadata = properties.UserMetadata
    };

    public static TopicDefinition ToDefinition(TopicProperties properties) => new()
    {
        Name = properties.Name,
        MaxSizeInMegabytes = properties.MaxSizeInMegabytes,
        MaxMessageSizeInKilobytes = properties.MaxMessageSizeInKilobytes,
        RequiresDuplicateDetection = properties.RequiresDuplicateDetection,
        DefaultMessageTimeToLive = FormatDuration(properties.DefaultMessageTimeToLive),
        AutoDeleteOnIdle = FormatDuration(properties.AutoDeleteOnIdle),
        DuplicateDetectionHistoryTimeWindow = FormatDuration(properties.DuplicateDetectionHistoryTimeWindow),
        EnableBatchedOperations = properties.EnableBatchedOperations,
        EnablePartitioning = properties.EnablePartitioning,
        SupportOrdering = properties.SupportOrdering,
        Status = properties.Status.ToString(),
        UserMetadata = properties.UserMetadata
    };

    public static SubscriptionDefinition ToDefinition(SubscriptionProperties properties) => new()
    {
        Name = properties.SubscriptionName,
        LockDuration = FormatDuration(properties.LockDuration),
        RequiresSession = properties.RequiresSession,
        DefaultMessageTimeToLive = FormatDuration(properties.DefaultMessageTimeToLive),
        AutoDeleteOnIdle = FormatDuration(properties.AutoDeleteOnIdle),
        DeadLetteringOnMessageExpiration = properties.DeadLetteringOnMessageExpiration,
        EnableDeadLetteringOnFilterEvaluationExceptions = properties.EnableDeadLetteringOnFilterEvaluationExceptions,
        MaxDeliveryCount = properties.MaxDeliveryCount,
        EnableBatchedOperations = properties.EnableBatchedOperations,
        Status = properties.Status.ToString(),
        ForwardTo = properties.ForwardTo,
        ForwardDeadLetteredMessagesTo = properties.ForwardDeadLetteredMessagesTo,
        UserMetadata = properties.UserMetadata
    };

    public static RuleDefinition ToDefinition(RuleProperties properties) =>
        ToDefinition(properties.Name, properties.Filter, properties.Action);

    /// <summary>
    /// Maps a rule's parts to a definition. Split out from the
    /// <see cref="RuleProperties"/> overload because that type has no public constructor —
    /// it only ever comes back from the service — so this is the form that can be tested.
    /// </summary>
    public static RuleDefinition ToDefinition(string name, RuleFilter? filter, RuleAction? action)
    {
        var definition = new RuleDefinition
        {
            Name = name,
            SqlAction = (action as SqlRuleAction)?.SqlExpression
        };

        // TrueRuleFilter and FalseRuleFilter both derive from SqlRuleFilter, so they have
        // to be matched before the general SQL case or they'd never be reached.
        switch (filter)
        {
            case TrueRuleFilter:
                definition.FilterType = "True";
                break;

            case FalseRuleFilter:
                definition.FilterType = "False";
                break;

            case SqlRuleFilter sql:
                definition.FilterType = "Sql";
                definition.SqlExpression = sql.SqlExpression;
                break;

            case CorrelationRuleFilter correlation:
                definition.FilterType = "Correlation";
                definition.CorrelationId = correlation.CorrelationId;
                definition.MessageId = correlation.MessageId;
                definition.To = correlation.To;
                definition.ReplyTo = correlation.ReplyTo;
                definition.Subject = correlation.Subject;
                definition.SessionId = correlation.SessionId;
                definition.ReplyToSessionId = correlation.ReplyToSessionId;
                definition.ContentType = correlation.ContentType;
                definition.ApplicationProperties = correlation.ApplicationProperties
                    .Select(pair => new RulePropertyDefinition { Key = pair.Key, Value = pair.Value?.ToString() })
                    .ToList();
                break;

            default:
                definition.FilterType = "True";
                break;
        }

        return definition;
    }

    public static CreateQueueOptions ToCreateOptions(QueueDefinition definition)
    {
        var options = new CreateQueueOptions(definition.Name)
        {
            RequiresDuplicateDetection = definition.RequiresDuplicateDetection,
            RequiresSession = definition.RequiresSession,
            DeadLetteringOnMessageExpiration = definition.DeadLetteringOnMessageExpiration,
            MaxDeliveryCount = definition.MaxDeliveryCount,
            EnableBatchedOperations = definition.EnableBatchedOperations,
            EnablePartitioning = definition.EnablePartitioning,
            Status = ParseStatus(definition.Status),
            UserMetadata = EmptyIfBlank(definition.UserMetadata)
        };

        if (definition.MaxSizeInMegabytes is { } maxSize)
        {
            options.MaxSizeInMegabytes = maxSize;
        }

        // Only meaningful on premium namespaces; leaving it unset lets the service default.
        if (definition.MaxMessageSizeInKilobytes is { } maxMessageSize)
        {
            options.MaxMessageSizeInKilobytes = maxMessageSize;
        }

        if (ParseDuration(definition.LockDuration) is { } lockDuration)
        {
            options.LockDuration = lockDuration;
        }

        if (ParseDuration(definition.DefaultMessageTimeToLive) is { } timeToLive)
        {
            options.DefaultMessageTimeToLive = timeToLive;
        }

        if (ParseDuration(definition.AutoDeleteOnIdle) is { } autoDeleteOnIdle)
        {
            options.AutoDeleteOnIdle = autoDeleteOnIdle;
        }

        // The service rejects this window unless duplicate detection is on.
        if (definition.RequiresDuplicateDetection &&
            ParseDuration(definition.DuplicateDetectionHistoryTimeWindow) is { } duplicateWindow)
        {
            options.DuplicateDetectionHistoryTimeWindow = duplicateWindow;
        }

        // Forwarding targets must already exist, so blank values are left alone rather
        // than sent as empty strings.
        if (!string.IsNullOrWhiteSpace(definition.ForwardTo))
        {
            options.ForwardTo = definition.ForwardTo;
        }

        if (!string.IsNullOrWhiteSpace(definition.ForwardDeadLetteredMessagesTo))
        {
            options.ForwardDeadLetteredMessagesTo = definition.ForwardDeadLetteredMessagesTo;
        }

        return options;
    }

    public static CreateTopicOptions ToCreateOptions(TopicDefinition definition)
    {
        var options = new CreateTopicOptions(definition.Name)
        {
            RequiresDuplicateDetection = definition.RequiresDuplicateDetection,
            EnableBatchedOperations = definition.EnableBatchedOperations,
            EnablePartitioning = definition.EnablePartitioning,
            SupportOrdering = definition.SupportOrdering,
            Status = ParseStatus(definition.Status),
            UserMetadata = EmptyIfBlank(definition.UserMetadata)
        };

        if (definition.MaxSizeInMegabytes is { } maxSize)
        {
            options.MaxSizeInMegabytes = maxSize;
        }

        if (definition.MaxMessageSizeInKilobytes is { } maxMessageSize)
        {
            options.MaxMessageSizeInKilobytes = maxMessageSize;
        }

        if (ParseDuration(definition.DefaultMessageTimeToLive) is { } timeToLive)
        {
            options.DefaultMessageTimeToLive = timeToLive;
        }

        if (ParseDuration(definition.AutoDeleteOnIdle) is { } autoDeleteOnIdle)
        {
            options.AutoDeleteOnIdle = autoDeleteOnIdle;
        }

        if (definition.RequiresDuplicateDetection &&
            ParseDuration(definition.DuplicateDetectionHistoryTimeWindow) is { } duplicateWindow)
        {
            options.DuplicateDetectionHistoryTimeWindow = duplicateWindow;
        }

        return options;
    }

    public static CreateSubscriptionOptions ToCreateOptions(string topicName, SubscriptionDefinition definition)
    {
        var options = new CreateSubscriptionOptions(topicName, definition.Name)
        {
            RequiresSession = definition.RequiresSession,
            DeadLetteringOnMessageExpiration = definition.DeadLetteringOnMessageExpiration,
            EnableDeadLetteringOnFilterEvaluationExceptions = definition.EnableDeadLetteringOnFilterEvaluationExceptions,
            MaxDeliveryCount = definition.MaxDeliveryCount,
            EnableBatchedOperations = definition.EnableBatchedOperations,
            Status = ParseStatus(definition.Status),
            UserMetadata = EmptyIfBlank(definition.UserMetadata)
        };

        if (ParseDuration(definition.LockDuration) is { } lockDuration)
        {
            options.LockDuration = lockDuration;
        }

        if (ParseDuration(definition.DefaultMessageTimeToLive) is { } timeToLive)
        {
            options.DefaultMessageTimeToLive = timeToLive;
        }

        if (ParseDuration(definition.AutoDeleteOnIdle) is { } autoDeleteOnIdle)
        {
            options.AutoDeleteOnIdle = autoDeleteOnIdle;
        }

        if (!string.IsNullOrWhiteSpace(definition.ForwardTo))
        {
            options.ForwardTo = definition.ForwardTo;
        }

        if (!string.IsNullOrWhiteSpace(definition.ForwardDeadLetteredMessagesTo))
        {
            options.ForwardDeadLetteredMessagesTo = definition.ForwardDeadLetteredMessagesTo;
        }

        return options;
    }

    public static CreateRuleOptions ToCreateOptions(RuleDefinition definition)
    {
        RuleFilter filter = definition.FilterType.ToLowerInvariant() switch
        {
            "correlation" => BuildCorrelationFilter(definition),
            "false" => new FalseRuleFilter(),
            "true" => new TrueRuleFilter(),
            _ => new SqlRuleFilter(string.IsNullOrWhiteSpace(definition.SqlExpression) ? "1=1" : definition.SqlExpression)
        };

        var options = new CreateRuleOptions(definition.Name, filter);

        if (!string.IsNullOrWhiteSpace(definition.SqlAction))
        {
            options.Action = new SqlRuleAction(definition.SqlAction);
        }

        return options;
    }

    static CorrelationRuleFilter BuildCorrelationFilter(RuleDefinition definition)
    {
        var filter = new CorrelationRuleFilter
        {
            CorrelationId = NullIfBlank(definition.CorrelationId),
            MessageId = NullIfBlank(definition.MessageId),
            To = NullIfBlank(definition.To),
            ReplyTo = NullIfBlank(definition.ReplyTo),
            Subject = NullIfBlank(definition.Subject),
            SessionId = NullIfBlank(definition.SessionId),
            ReplyToSessionId = NullIfBlank(definition.ReplyToSessionId),
            ContentType = NullIfBlank(definition.ContentType)
        };

        foreach (var property in definition.ApplicationProperties)
        {
            filter.ApplicationProperties[property.Key] = property.Value;
        }

        return filter;
    }

    /// <summary>
    /// Copies the settings Service Bus permits changing after creation. Partitioning,
    /// sessions and duplicate detection are fixed at creation time and are skipped —
    /// sending them on an update is rejected by the service.
    /// </summary>
    static void ApplyUpdatable(QueueDefinition definition, QueueProperties target)
    {
        if (ParseDuration(definition.LockDuration) is { } lockDuration)
        {
            target.LockDuration = lockDuration;
        }

        if (ParseDuration(definition.DefaultMessageTimeToLive) is { } timeToLive)
        {
            target.DefaultMessageTimeToLive = timeToLive;
        }

        if (ParseDuration(definition.AutoDeleteOnIdle) is { } autoDeleteOnIdle)
        {
            target.AutoDeleteOnIdle = autoDeleteOnIdle;
        }

        target.DeadLetteringOnMessageExpiration = definition.DeadLetteringOnMessageExpiration;
        target.MaxDeliveryCount = definition.MaxDeliveryCount;
        target.EnableBatchedOperations = definition.EnableBatchedOperations;
        target.Status = ParseStatus(definition.Status);
        target.UserMetadata = EmptyIfBlank(definition.UserMetadata);
        target.ForwardTo = NullIfBlank(definition.ForwardTo);
        target.ForwardDeadLetteredMessagesTo = NullIfBlank(definition.ForwardDeadLetteredMessagesTo);

        if (definition.MaxSizeInMegabytes is { } maxSize)
        {
            target.MaxSizeInMegabytes = maxSize;
        }
    }

    static void ApplyUpdatable(TopicDefinition definition, TopicProperties target)
    {
        if (ParseDuration(definition.DefaultMessageTimeToLive) is { } timeToLive)
        {
            target.DefaultMessageTimeToLive = timeToLive;
        }

        if (ParseDuration(definition.AutoDeleteOnIdle) is { } autoDeleteOnIdle)
        {
            target.AutoDeleteOnIdle = autoDeleteOnIdle;
        }

        target.EnableBatchedOperations = definition.EnableBatchedOperations;
        target.Status = ParseStatus(definition.Status);
        target.UserMetadata = EmptyIfBlank(definition.UserMetadata);

        if (definition.MaxSizeInMegabytes is { } maxSize)
        {
            target.MaxSizeInMegabytes = maxSize;
        }
    }

    static void ApplyUpdatable(SubscriptionDefinition definition, SubscriptionProperties target)
    {
        if (ParseDuration(definition.LockDuration) is { } lockDuration)
        {
            target.LockDuration = lockDuration;
        }

        if (ParseDuration(definition.DefaultMessageTimeToLive) is { } timeToLive)
        {
            target.DefaultMessageTimeToLive = timeToLive;
        }

        if (ParseDuration(definition.AutoDeleteOnIdle) is { } autoDeleteOnIdle)
        {
            target.AutoDeleteOnIdle = autoDeleteOnIdle;
        }

        target.DeadLetteringOnMessageExpiration = definition.DeadLetteringOnMessageExpiration;
        target.EnableDeadLetteringOnFilterEvaluationExceptions = definition.EnableDeadLetteringOnFilterEvaluationExceptions;
        target.MaxDeliveryCount = definition.MaxDeliveryCount;
        target.EnableBatchedOperations = definition.EnableBatchedOperations;
        target.Status = ParseStatus(definition.Status);
        target.UserMetadata = EmptyIfBlank(definition.UserMetadata);
        target.ForwardTo = NullIfBlank(definition.ForwardTo);
        target.ForwardDeadLetteredMessagesTo = NullIfBlank(definition.ForwardDeadLetteredMessagesTo);
    }

    /// <summary>
    /// Writes a duration as ISO 8601. <see cref="TimeSpan.MaxValue"/> is Service Bus's
    /// "never" sentinel and round-trips as null so an import leaves the service default in place.
    /// </summary>
    static string? FormatDuration(TimeSpan value) =>
        value == TimeSpan.MaxValue || value == TimeSpan.Zero ? null : XmlConvert.ToString(value);

    static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return XmlConvert.ToTimeSpan(value);
        }
        catch (FormatException)
        {
            // Fall back to .NET's own "d.hh:mm:ss" shape, which older exports may contain.
            return TimeSpan.TryParse(value, out var parsed) ? parsed : null;
        }
    }

    /// <summary>The statuses the service actually accepts on a create or update.</summary>
    static readonly EntityStatus[] KnownStatuses =
    [
        EntityStatus.Active,
        EntityStatus.Disabled,
        EntityStatus.SendDisabled,
        EntityStatus.ReceiveDisabled
    ];

    /// <summary>
    /// Reads a status name from a definition file.
    /// </summary>
    /// <remarks>
    /// <see cref="EntityStatus"/> looks like an enum but is a struct wrapping a string, so
    /// <c>Enum.TryParse</c> throws on it. Its string constructor accepts anything, which
    /// would let a typo through to the service, so the value is matched against the known
    /// set and anything unrecognised falls back to Active.
    /// </remarks>
    public static EntityStatus ParseStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return EntityStatus.Active;
        }

        foreach (var status in KnownStatuses)
        {
            if (status.ToString().Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return status;
            }
        }

        return EntityStatus.Active;
    }

    /// <summary>
    /// Metadata setters on the SDK's option and property types reject null outright, so a
    /// definition without metadata has to clear the field with an empty string instead.
    /// </summary>
    public static string EmptyIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value;

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
