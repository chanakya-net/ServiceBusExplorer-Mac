using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.ServiceBus;

using SbMac.Core.Messaging;

namespace SbMac.Core.EventHubs;

/// <summary>
/// Translates between Event Hubs events and the message shapes the rest of the app
/// already speaks, so the grid, body viewer and compose dialog work for both services.
/// </summary>
public static class EventMapper
{
    /// <summary>Copies an event hub's description into the shape the tree binds to.</summary>
    public static EventHubEntity ToEntity(EventHubProperties properties, IReadOnlyList<string> consumerGroups) =>
        new(properties.Name, properties.CreatedOn, properties.PartitionIds, consumerGroups);

    /// <summary>Copies a partition's live cursor into the shape the tree binds to.</summary>
    public static PartitionEntity ToEntity(string eventHubName, PartitionProperties properties) =>
        new(
            eventHubName,
            properties.Id,
            properties.BeginningSequenceNumber,
            properties.LastEnqueuedSequenceNumber,
            properties.LastEnqueuedTime,
            properties.IsEmpty);

    /// <summary>
    /// Copies an event into a detached record. <paramref name="partitionId"/> comes from
    /// the partition that produced it rather than from the event, which does not carry it.
    /// </summary>
    public static MessageRecord ToRecord(EventData data, string eventHubName, string partitionId)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in data.Properties)
        {
            properties[pair.Key] = pair.Value;
        }

        return new MessageRecord
        {
            Body = data.EventBody,
            MessageId = data.MessageId ?? string.Empty,
            SequenceNumber = data.SequenceNumber,
            EnqueuedTime = data.EnqueuedTime,
            PartitionKey = data.PartitionKey,
            CorrelationId = data.CorrelationId,
            ContentType = data.ContentType,
            ApplicationProperties = properties,
            SourceEntityPath = eventHubName,
            // Offsets are opaque strings rather than numbers — geo-replication made them
            // non-numeric — so the value is only ever displayed, never arithmetic.
            Event = new EventOrigin(partitionId, data.OffsetString)
        };
    }

    /// <summary>
    /// Turns a composed message into an event.
    /// </summary>
    /// <remarks>
    /// The compose dialog builds <see cref="ServiceBusMessage"/> values, and most of what
    /// it collects has a direct equivalent on an event. What does not — sessions, time to
    /// live, scheduling, reply-to routing — is dropped rather than faked, because Event
    /// Hubs has no broker-side behaviour to attach it to. The partition key is not copied
    /// here either: on the Event Hubs side it belongs to the batch, not the event, so
    /// <see cref="EventHubService.SendAsync"/> takes it separately.
    /// </remarks>
    public static EventData ToEventData(ServiceBusMessage message)
    {
        var data = new EventData(message.Body)
        {
            ContentType = message.ContentType,
            CorrelationId = message.CorrelationId
        };

        if (!string.IsNullOrEmpty(message.MessageId))
        {
            data.MessageId = message.MessageId;
        }

        foreach (var pair in message.ApplicationProperties)
        {
            data.Properties[pair.Key] = pair.Value;
        }

        // The compose dialog's "label" is a Service Bus concept. Event Hubs consumers
        // conventionally read it from a user property, so it is carried across as one
        // instead of being lost.
        if (!string.IsNullOrEmpty(message.Subject))
        {
            data.Properties["Subject"] = message.Subject;
        }

        return data;
    }
}
