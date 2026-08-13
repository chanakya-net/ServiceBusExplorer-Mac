using Azure.Messaging.ServiceBus;

namespace SbMac.Core.Messaging;

/// <summary>
/// A detached snapshot of a received message.
/// </summary>
/// <remarks>
/// <see cref="ServiceBusReceivedMessage"/> is tied to the receiver that produced it,
/// and peek receivers are disposed as soon as a page is read. Copying what we need
/// lets the UI keep showing messages after the receiver is gone.
/// </remarks>
public sealed class MessageRecord
{
    public required BinaryData Body { get; init; }

    public string MessageId { get; init; } = string.Empty;

    public long SequenceNumber { get; init; }

    public long EnqueuedSequenceNumber { get; init; }

    public DateTimeOffset EnqueuedTime { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset ScheduledEnqueueTime { get; init; }

    public int DeliveryCount { get; init; }

    public string? SessionId { get; init; }

    public string? PartitionKey { get; init; }

    public string? CorrelationId { get; init; }

    public string? Subject { get; init; }

    public string? To { get; init; }

    public string? ReplyTo { get; init; }

    public string? ReplyToSessionId { get; init; }

    public string? ContentType { get; init; }

    public TimeSpan TimeToLive { get; init; }

    public string? DeadLetterReason { get; init; }

    public string? DeadLetterErrorDescription { get; init; }

    public string? DeadLetterSource { get; init; }

    public ServiceBusMessageState State { get; init; }

    /// <summary>Custom application properties, in insertion order.</summary>
    public IReadOnlyDictionary<string, object?> ApplicationProperties { get; init; } =
        new Dictionary<string, object?>();

    /// <summary>Where this message was read from — used when resubmitting or deleting it.</summary>
    public required string SourceEntityPath { get; init; }

    /// <summary>True when the message came from a dead-letter sub-queue.</summary>
    public bool IsDeadLetter { get; init; }

    public long SizeInBytes => Body.ToMemory().Length;

    /// <summary>The body decoded for display, with the encoding that was used to do it.</summary>
    public MessageBodyView BodyView => MessageBodyDecoder.Decode(Body);

    public static MessageRecord From(ServiceBusReceivedMessage message, string sourceEntityPath, bool isDeadLetter)
    {
        var applicationProperties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in message.ApplicationProperties)
        {
            applicationProperties[pair.Key] = pair.Value;
        }

        return new MessageRecord
        {
            Body = message.Body,
            MessageId = message.MessageId ?? string.Empty,
            SequenceNumber = message.SequenceNumber,
            EnqueuedSequenceNumber = message.EnqueuedSequenceNumber,
            EnqueuedTime = message.EnqueuedTime,
            ExpiresAt = message.ExpiresAt,
            ScheduledEnqueueTime = message.ScheduledEnqueueTime,
            DeliveryCount = message.DeliveryCount,
            SessionId = message.SessionId,
            PartitionKey = message.PartitionKey,
            CorrelationId = message.CorrelationId,
            Subject = message.Subject,
            To = message.To,
            ReplyTo = message.ReplyTo,
            ReplyToSessionId = message.ReplyToSessionId,
            ContentType = message.ContentType,
            TimeToLive = message.TimeToLive,
            DeadLetterReason = message.DeadLetterReason,
            DeadLetterErrorDescription = message.DeadLetterErrorDescription,
            DeadLetterSource = message.DeadLetterSource,
            State = message.State,
            ApplicationProperties = applicationProperties,
            SourceEntityPath = sourceEntityPath,
            IsDeadLetter = isDeadLetter
        };
    }

    /// <summary>
    /// Builds a sendable copy of this message, carrying every user-set property across.
    /// Broker-owned values (sequence number, enqueue time, delivery count) are left for
    /// the service to assign.
    /// </summary>
    public ServiceBusMessage ToSendableMessage(bool preserveMessageId = false)
    {
        var message = new ServiceBusMessage(Body)
        {
            CorrelationId = CorrelationId,
            Subject = Subject,
            To = To,
            ReplyTo = ReplyTo,
            ReplyToSessionId = ReplyToSessionId,
            ContentType = ContentType,
            SessionId = SessionId
        };

        // On a session-enabled entity the broker derives the partition key from the
        // session id and reports the two as the same value. Assigning it back throws
        // unless it matches, so it's only set when there's no session to conflict with.
        if (SessionId is null && PartitionKey is not null)
        {
            message.PartitionKey = PartitionKey;
        }

        // Reusing the original id defeats duplicate detection on a resubmit, so it's opt-in.
        if (preserveMessageId && !string.IsNullOrEmpty(MessageId))
        {
            message.MessageId = MessageId;
        }

        // TimeToLive must stay unset when it's the entity default; assigning
        // TimeSpan.MaxValue explicitly is rejected by the service.
        if (TimeToLive > TimeSpan.Zero && TimeToLive < TimeSpan.MaxValue)
        {
            message.TimeToLive = TimeToLive;
        }

        foreach (var pair in ApplicationProperties)
        {
            message.ApplicationProperties[pair.Key] = pair.Value;
        }

        return message;
    }
}
