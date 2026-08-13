using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Azure.ResourceManager;
using Azure.ResourceManager.EventHubs;
using Azure.ResourceManager.Resources;

using SbMac.Core.Connections;
using SbMac.Core.Messaging;

namespace SbMac.Core.EventHubs;

/// <summary>
/// Browse and write to the event hubs in a namespace.
/// </summary>
/// <remarks>
/// Reading here is always non-destructive. Event Hubs is an append-only log with
/// time-based retention: consumers hold their own cursor and nothing they do removes
/// events, so there is no equivalent of Service Bus's receive-and-delete, dead-letter
/// queue or purge. Every read in this class is the Event Hubs analogue of peek.
/// </remarks>
public sealed class EventHubService
{
    /// <summary>
    /// How long a partition read waits for the next event before concluding it has caught
    /// up. A read that has reached the end of the log never completes on its own — the
    /// stream stays open waiting for new events — so this is what ends it.
    /// </summary>
    static readonly TimeSpan ReadWaitTime = TimeSpan.FromSeconds(5);

    readonly EventHubsSession session;

    /// <summary>
    /// Built on first discovery and kept. Rebuilding it would rebuild the credential too,
    /// and an interactive credential rebuilt on every refresh means a browser window on
    /// every refresh.
    /// </summary>
    ArmClient? armClient;

    public EventHubService(EventHubsSession session)
    {
        this.session = session;
    }

    // ------------------------------------------------------------ event hubs

    /// <summary>
    /// Lists the namespace's event hubs, with their partitions and consumer groups.
    /// </summary>
    /// <remarks>
    /// Enumeration is a management-plane operation, so it is tried through ARM first and
    /// falls back to the names saved on the connection when the signed-in identity cannot
    /// read the namespace's resource — which is always the case for a SAS key. Each hub is
    /// then described over the data plane to pick up its partitions.
    /// </remarks>
    public async Task<IReadOnlyList<EventHubEntity>> GetEventHubsAsync(CancellationToken cancellationToken = default)
    {
        var discovered = await TryDiscoverAsync(cancellationToken).ConfigureAwait(false);

        // The configured names are merged in rather than replaced: a hub created after the
        // last ARM read, or one the identity cannot see in ARM but can reach over AMQP,
        // should still appear.
        var names = new List<string>();
        var consumerGroups = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var hub in discovered ?? [])
        {
            names.Add(hub.Name);
            consumerGroups[hub.Name] = hub.ConsumerGroups;
        }

        foreach (var name in session.Connection.ConfiguredEventHubNames)
        {
            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(name);
            }
        }

        var entities = new List<EventHubEntity>(names.Count);
        foreach (var name in names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            entities.Add(await DescribeAsync(
                name,
                consumerGroups.GetValueOrDefault(name, []),
                cancellationToken).ConfigureAwait(false));
        }

        return entities;
    }

    /// <summary>Re-reads one hub's partitions, keeping the consumer groups already known.</summary>
    public Task<EventHubEntity> GetEventHubAsync(
        string eventHubName,
        IReadOnlyList<string>? knownConsumerGroups = null,
        CancellationToken cancellationToken = default) =>
        DescribeAsync(eventHubName, knownConsumerGroups ?? [], cancellationToken);

    async Task<EventHubEntity> DescribeAsync(
        string eventHubName,
        IReadOnlyList<string> consumerGroups,
        CancellationToken cancellationToken)
    {
        try
        {
            var properties = await session.Consumer(eventHubName)
                .GetEventHubPropertiesAsync(cancellationToken)
                .ConfigureAwait(false);

            return EventMapper.ToEntity(properties, consumerGroups);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // One unreachable hub — misspelled, deleted, or not covered by the SAS rule —
            // must not take the rest of the namespace down with it.
            return EventHubEntity.Unreachable(eventHubName, exception.Message);
        }
    }

    // ------------------------------------------------------------- partitions

    public async Task<IReadOnlyList<PartitionEntity>> GetPartitionsAsync(
        string eventHubName,
        CancellationToken cancellationToken = default)
    {
        var consumer = session.Consumer(eventHubName);
        var properties = await consumer.GetEventHubPropertiesAsync(cancellationToken).ConfigureAwait(false);

        var partitions = new List<PartitionEntity>(properties.PartitionIds.Length);
        foreach (var partitionId in properties.PartitionIds)
        {
            partitions.Add(await GetPartitionAsync(eventHubName, partitionId, cancellationToken).ConfigureAwait(false));
        }

        return partitions;
    }

    public async Task<PartitionEntity> GetPartitionAsync(
        string eventHubName,
        string partitionId,
        CancellationToken cancellationToken = default)
    {
        var properties = await session.Consumer(eventHubName)
            .GetPartitionPropertiesAsync(partitionId, cancellationToken)
            .ConfigureAwait(false);

        return EventMapper.ToEntity(eventHubName, properties);
    }

    // ------------------------------------------------------------------ read

    /// <summary>
    /// Reads the most recent events without consuming them.
    /// </summary>
    /// <param name="eventHubName">The hub to read from.</param>
    /// <param name="partitionId">A single partition, or null to read across every partition.</param>
    /// <param name="count">
    /// How many events to return in total. Across a whole hub the budget is split evenly
    /// between partitions, because there is no namespace-wide ordering to take a global
    /// "last N" from — each partition is its own log.
    /// </param>
    public async Task<IReadOnlyList<MessageRecord>> PeekAsync(
        string eventHubName,
        string? partitionId,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            return [];
        }

        var consumer = session.Consumer(eventHubName);

        string[] partitionIds;
        if (partitionId is not null)
        {
            partitionIds = [partitionId];
        }
        else
        {
            var properties = await consumer.GetEventHubPropertiesAsync(cancellationToken).ConfigureAwait(false);
            partitionIds = properties.PartitionIds;
        }

        if (partitionIds.Length == 0)
        {
            return [];
        }

        var perPartition = PerPartitionBudget(count, partitionIds.Length);
        var records = new List<MessageRecord>(Math.Min(count, perPartition * partitionIds.Length));

        foreach (var id in partitionIds)
        {
            records.AddRange(await ReadPartitionAsync(consumer, eventHubName, id, perPartition, cancellationToken)
                .ConfigureAwait(false));
        }

        // Partitions are read one after another, so the combined list is grouped by
        // partition. Ordering by enqueue time is what makes it read as one stream.
        return records
            .OrderBy(record => record.EnqueuedTime)
            .ThenBy(record => record.SequenceNumber)
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// How many events to read from each partition when reading across a whole hub.
    /// </summary>
    /// <remarks>
    /// Rounded up, so a request never comes back short because the count didn't divide
    /// evenly; the surplus is trimmed once the partitions are merged.
    /// </remarks>
    public static int PerPartitionBudget(int count, int partitionCount) =>
        partitionCount <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(count / (double)partitionCount));

    /// <summary>
    /// The sequence number a "last <paramref name="count"/> events" read starts at.
    /// </summary>
    /// <remarks>
    /// The window is inclusive at both ends, so covering N events means starting N-1 back
    /// from the last one. It is clamped to the oldest event still retained, because a
    /// sequence number that has aged out is rejected rather than rounded up.
    /// </remarks>
    public static long StartSequenceNumber(long beginningSequenceNumber, long lastEnqueuedSequenceNumber, int count) =>
        Math.Max(beginningSequenceNumber, lastEnqueuedSequenceNumber - count + 1);

    async Task<IReadOnlyList<MessageRecord>> ReadPartitionAsync(
        EventHubConsumerClient consumer,
        string eventHubName,
        string partitionId,
        int count,
        CancellationToken cancellationToken)
    {
        var properties = await consumer
            .GetPartitionPropertiesAsync(partitionId, cancellationToken)
            .ConfigureAwait(false);

        if (properties.IsEmpty)
        {
            return [];
        }

        var start = StartSequenceNumber(
            properties.BeginningSequenceNumber,
            properties.LastEnqueuedSequenceNumber,
            count);

        var options = new ReadEventOptions { MaximumWaitTime = ReadWaitTime };
        var records = new List<MessageRecord>(count);

        await foreach (var partitionEvent in consumer
            .ReadEventsFromPartitionAsync(
                partitionId,
                EventPosition.FromSequenceNumber(start, isInclusive: true),
                options,
                cancellationToken)
            .ConfigureAwait(false))
        {
            // A null payload is how the SDK signals that MaximumWaitTime elapsed with
            // nothing new — the read has caught up with the end of the log.
            if (partitionEvent.Data is null)
            {
                break;
            }

            records.Add(EventMapper.ToRecord(partitionEvent.Data, eventHubName, partitionId));

            if (records.Count >= count)
            {
                break;
            }
        }

        return records;
    }

    // ----------------------------------------------------------------- write

    /// <summary>
    /// Publishes events, packing them into service-sized batches.
    /// </summary>
    /// <param name="partitionKey">
    /// Hashes to a partition and keeps events sharing the key in order. Mutually exclusive
    /// with <paramref name="partitionId"/>.
    /// </param>
    /// <param name="partitionId">Publishes straight to one partition, bypassing the hash.</param>
    /// <returns>The number of events published.</returns>
    /// <exception cref="InvalidOperationException">A single event is too large for an empty batch.</exception>
    public async Task<int> SendAsync(
        string eventHubName,
        IReadOnlyList<EventData> events,
        string? partitionKey = null,
        string? partitionId = null,
        CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(partitionKey) && !string.IsNullOrWhiteSpace(partitionId))
        {
            throw new InvalidOperationException(
                "An event can be routed by partition key or sent to a specific partition, but not both.");
        }

        var producer = session.Producer(eventHubName);

        var batchOptions = new CreateBatchOptions
        {
            PartitionKey = NullIfBlank(partitionKey),
            PartitionId = NullIfBlank(partitionId)
        };

        var sent = 0;
        var index = 0;

        while (index < events.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var batch = await producer.CreateBatchAsync(batchOptions, cancellationToken).ConfigureAwait(false);

            while (index < events.Count && batch.TryAdd(events[index]))
            {
                index++;
            }

            if (batch.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Event {index + 1} is too large to send: it exceeds the maximum batch size for '{eventHubName}'.");
            }

            await producer.SendAsync(batch, cancellationToken).ConfigureAwait(false);
            sent += batch.Count;
        }

        return sent;
    }

    // ------------------------------------------------------------- discovery

    /// <summary>
    /// Lists the namespace's hubs through ARM.
    /// </summary>
    /// <returns>
    /// Null when discovery isn't possible — SAS authentication, or an identity without
    /// read access to the namespace resource. That is an expected outcome, not a failure:
    /// the caller falls back to the names saved on the connection.
    /// </returns>
    async Task<IReadOnlyList<EventHubEntity>?> TryDiscoverAsync(CancellationToken cancellationToken)
    {
        var connection = session.Connection;

        // ARM has no way to accept a SAS key, so there is nothing to try for those.
        if (connection.AuthenticationMode != AuthenticationMode.EntraId)
        {
            return null;
        }

        var host = connection.ResolvedNamespace;
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        // The ARM resource is named with the bare namespace, not the DNS host.
        var namespaceName = host.Split('.')[0];

        try
        {
            var arm = armClient ??= new ArmClient(CredentialFactory.Create(connection));

            var subscription = string.IsNullOrWhiteSpace(connection.SubscriptionId)
                ? await arm.GetDefaultSubscriptionAsync(cancellationToken).ConfigureAwait(false)
                : arm.GetSubscriptionResource(
                    SubscriptionResource.CreateResourceIdentifier(connection.SubscriptionId.Trim()));

            await foreach (var candidate in subscription
                .GetEventHubsNamespacesAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (!string.Equals(candidate.Data.Name, namespaceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return await ReadHubsAsync(candidate, cancellationToken).ConfigureAwait(false);
            }

            // The subscription is readable but holds no namespace by that name, so there
            // is nothing to discover here either.
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static async Task<IReadOnlyList<EventHubEntity>> ReadHubsAsync(
        EventHubsNamespaceResource namespaceResource,
        CancellationToken cancellationToken)
    {
        var hubs = new List<EventHubEntity>();

        await foreach (var hub in namespaceResource
            .GetEventHubs()
            .GetAllAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false))
        {
            var groups = new List<string>();
            await foreach (var group in hub
                .GetEventHubsConsumerGroups()
                .GetAllAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))
            {
                groups.Add(group.Data.Name);
            }

            groups.Sort(StringComparer.OrdinalIgnoreCase);

            // Partitions come from the data plane; this record only carries identity.
            hubs.Add(new EventHubEntity(hub.Data.Name, default, [], groups));
        }

        return hubs;
    }

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
