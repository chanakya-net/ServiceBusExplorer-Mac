using System.Xml.Serialization;

namespace SbMac.Core.ImportExport;

/// <summary>
/// Serializable snapshot of a namespace's entity definitions.
/// </summary>
/// <remarks>
/// Durations are held as ISO 8601 strings rather than <see cref="TimeSpan"/> so the
/// same DTOs round-trip cleanly through both XML and JSON, and so a file written by
/// one version stays readable when a property is added later.
/// </remarks>
[XmlRoot("ServiceBusNamespace")]
public sealed class NamespaceDefinition
{
    /// <summary>Namespace the definitions were exported from. Informational only; import ignores it.</summary>
    [XmlAttribute("sourceNamespace")]
    public string? SourceNamespace { get; set; }

    [XmlAttribute("exportedAt")]
    public string? ExportedAt { get; set; }

    [XmlArray("Queues")]
    [XmlArrayItem("Queue")]
    public List<QueueDefinition> Queues { get; set; } = [];

    [XmlArray("Topics")]
    [XmlArrayItem("Topic")]
    public List<TopicDefinition> Topics { get; set; } = [];
}

public sealed class QueueDefinition
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    public string? LockDuration { get; set; }
    public long? MaxSizeInMegabytes { get; set; }
    public long? MaxMessageSizeInKilobytes { get; set; }
    public bool RequiresDuplicateDetection { get; set; }
    public bool RequiresSession { get; set; }
    public string? DefaultMessageTimeToLive { get; set; }
    public string? AutoDeleteOnIdle { get; set; }
    public bool DeadLetteringOnMessageExpiration { get; set; }
    public string? DuplicateDetectionHistoryTimeWindow { get; set; }
    public int MaxDeliveryCount { get; set; } = 10;
    public bool EnableBatchedOperations { get; set; } = true;
    public bool EnablePartitioning { get; set; }
    public string? Status { get; set; }
    public string? ForwardTo { get; set; }
    public string? ForwardDeadLetteredMessagesTo { get; set; }
    public string? UserMetadata { get; set; }
}

public sealed class TopicDefinition
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    public long? MaxSizeInMegabytes { get; set; }
    public long? MaxMessageSizeInKilobytes { get; set; }
    public bool RequiresDuplicateDetection { get; set; }
    public string? DefaultMessageTimeToLive { get; set; }
    public string? AutoDeleteOnIdle { get; set; }
    public string? DuplicateDetectionHistoryTimeWindow { get; set; }
    public bool EnableBatchedOperations { get; set; } = true;
    public bool EnablePartitioning { get; set; }
    public bool SupportOrdering { get; set; } = true;
    public string? Status { get; set; }
    public string? UserMetadata { get; set; }

    [XmlArray("Subscriptions")]
    [XmlArrayItem("Subscription")]
    public List<SubscriptionDefinition> Subscriptions { get; set; } = [];
}

public sealed class SubscriptionDefinition
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    public string? LockDuration { get; set; }
    public bool RequiresSession { get; set; }
    public string? DefaultMessageTimeToLive { get; set; }
    public string? AutoDeleteOnIdle { get; set; }
    public bool DeadLetteringOnMessageExpiration { get; set; }
    public bool EnableDeadLetteringOnFilterEvaluationExceptions { get; set; } = true;
    public int MaxDeliveryCount { get; set; } = 10;
    public bool EnableBatchedOperations { get; set; } = true;
    public string? Status { get; set; }
    public string? ForwardTo { get; set; }
    public string? ForwardDeadLetteredMessagesTo { get; set; }
    public string? UserMetadata { get; set; }

    [XmlArray("Rules")]
    [XmlArrayItem("Rule")]
    public List<RuleDefinition> Rules { get; set; } = [];
}

public sealed class RuleDefinition
{
    [XmlAttribute("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>One of <c>Sql</c>, <c>Correlation</c>, <c>True</c> or <c>False</c>.</summary>
    [XmlAttribute("filterType")]
    public string FilterType { get; set; } = "Sql";

    /// <summary>The filter text for a SQL filter, e.g. <c>region = 'emea'</c>.</summary>
    public string? SqlExpression { get; set; }

    /// <summary>Optional SQL action applied to matching messages.</summary>
    public string? SqlAction { get; set; }

    // Correlation filter fields. All are optional and combine with AND.
    public string? CorrelationId { get; set; }
    public string? MessageId { get; set; }
    public string? To { get; set; }
    public string? ReplyTo { get; set; }
    public string? Subject { get; set; }
    public string? SessionId { get; set; }
    public string? ReplyToSessionId { get; set; }
    public string? ContentType { get; set; }

    [XmlArray("ApplicationProperties")]
    [XmlArrayItem("Property")]
    public List<RulePropertyDefinition> ApplicationProperties { get; set; } = [];
}

public sealed class RulePropertyDefinition
{
    [XmlAttribute("key")]
    public string Key { get; set; } = string.Empty;

    [XmlAttribute("value")]
    public string? Value { get; set; }
}
