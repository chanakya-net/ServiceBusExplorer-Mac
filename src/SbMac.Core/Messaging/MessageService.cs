using Azure.Messaging.ServiceBus;

using SbMac.Core.Entities;

namespace SbMac.Core.Messaging;

/// <summary>Progress report emitted while a long-running purge is draining an entity.</summary>
public readonly record struct PurgeProgress(long DeletedCount, string EntityPath, bool DeadLetter);

/// <summary>
/// Reads, writes and deletes messages on queues, subscriptions and their
/// dead-letter sub-queues.
/// </summary>
public sealed class MessageService
{
    /// <summary>
    /// How long to wait for a batch before concluding an entity is drained. Service Bus
    /// can return an empty batch while messages remain, so callers that must not stop
    /// early rely on the consecutive-empty-batch rule in <see cref="PurgeAsync"/>.
    /// </summary>
    static readonly TimeSpan BatchWaitTime = TimeSpan.FromSeconds(5);

    /// <summary>The service caps a single receive at 250 messages regardless of what we ask for.</summary>
    const int MaxBatchSize = 250;

    readonly ServiceBusSession session;

    public MessageService(ServiceBusSession session)
    {
        this.session = session;
    }

    // ------------------------------------------------------------------ read

    /// <summary>
    /// Reads messages without removing or locking them. Safe to run against production.
    /// </summary>
    /// <param name="entity">Queue or subscription to read from.</param>
    /// <param name="deadLetter">Read the dead-letter sub-queue instead of the main entity.</param>
    /// <param name="count">Maximum number of messages to return.</param>
    /// <param name="fromSequenceNumber">Start at this sequence number; null starts from the beginning.</param>
    public async Task<IReadOnlyList<MessageRecord>> PeekAsync(
        EntityPath entity,
        bool deadLetter,
        int count,
        long? fromSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        await using var receiver = CreateReceiver(entity, deadLetter, ServiceBusReceiveMode.PeekLock);

        var results = new List<MessageRecord>(Math.Min(count, MaxBatchSize));
        var cursor = fromSequenceNumber;

        // PeekMessagesAsync returns at most a page at a time, so loop until we have
        // what was asked for or the entity runs out.
        while (results.Count < count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wanted = Math.Min(count - results.Count, MaxBatchSize);
            var batch = await receiver
                .PeekMessagesAsync(wanted, cursor, cancellationToken)
                .ConfigureAwait(false);

            if (batch is null || batch.Count == 0)
            {
                break;
            }

            foreach (var message in batch)
            {
                results.Add(MessageRecord.From(message, entity.ToString(), deadLetter));
            }

            // Resume just past the last message we saw.
            cursor = batch[^1].SequenceNumber + 1;
        }

        return results;
    }

    /// <summary>
    /// Receives and permanently removes messages. This is destructive — the messages are
    /// gone from the entity when this returns.
    /// </summary>
    public async Task<IReadOnlyList<MessageRecord>> ReceiveAndDeleteAsync(
        EntityPath entity,
        bool deadLetter,
        int count,
        CancellationToken cancellationToken = default)
    {
        await using var receiver = CreateReceiver(entity, deadLetter, ServiceBusReceiveMode.ReceiveAndDelete);

        var results = new List<MessageRecord>(Math.Min(count, MaxBatchSize));

        while (results.Count < count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var wanted = Math.Min(count - results.Count, MaxBatchSize);
            var batch = await receiver
                .ReceiveMessagesAsync(wanted, BatchWaitTime, cancellationToken)
                .ConfigureAwait(false);

            if (batch is null || batch.Count == 0)
            {
                break;
            }

            foreach (var message in batch)
            {
                results.Add(MessageRecord.From(message, entity.ToString(), deadLetter));
            }
        }

        return results;
    }

    // ----------------------------------------------------------------- write

    /// <summary>
    /// Sends messages to a queue or topic, packing them into service-sized batches.
    /// </summary>
    /// <returns>The number of messages sent.</returns>
    /// <exception cref="InvalidOperationException">A single message is too large for an empty batch.</exception>
    public async Task<int> SendAsync(
        string queueOrTopicName,
        IReadOnlyList<ServiceBusMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return 0;
        }

        await using var sender = session.Messaging.CreateSender(queueOrTopicName);

        var sent = 0;
        var index = 0;

        while (index < messages.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var batch = await sender.CreateMessageBatchAsync(cancellationToken).ConfigureAwait(false);

            // Fill the batch until the next message won't fit, then ship it.
            while (index < messages.Count && batch.TryAddMessage(messages[index]))
            {
                index++;
            }

            if (batch.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Message {index + 1} is too large to send: it exceeds the maximum batch size for '{queueOrTopicName}'.");
            }

            await sender.SendMessagesAsync(batch, cancellationToken).ConfigureAwait(false);
            sent += batch.Count;
        }

        return sent;
    }

    /// <summary>Schedules a message for future delivery. Returns the sequence number needed to cancel it.</summary>
    public async Task<long> ScheduleAsync(
        string queueOrTopicName,
        ServiceBusMessage message,
        DateTimeOffset enqueueTime,
        CancellationToken cancellationToken = default)
    {
        await using var sender = session.Messaging.CreateSender(queueOrTopicName);
        return await sender.ScheduleMessageAsync(message, enqueueTime, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cancels messages previously scheduled with <see cref="ScheduleAsync"/>.</summary>
    public async Task CancelScheduledAsync(
        string queueOrTopicName,
        IEnumerable<long> sequenceNumbers,
        CancellationToken cancellationToken = default)
    {
        await using var sender = session.Messaging.CreateSender(queueOrTopicName);
        foreach (var sequenceNumber in sequenceNumbers)
        {
            await sender.CancelScheduledMessageAsync(sequenceNumber, cancellationToken).ConfigureAwait(false);
        }
    }

    // ----------------------------------------------------------- dead-letter

    /// <summary>
    /// Sends copies of <paramref name="messages"/> back to a live entity — the standard
    /// fix-and-retry move for dead-lettered messages.
    /// </summary>
    /// <param name="targetEntityName">Queue or topic to resubmit to.</param>
    /// <param name="removeFromDeadLetter">
    /// Also delete the originals from the dead-letter queue. Resubmit happens first, so a
    /// failure to send leaves the dead-letter copy untouched.
    /// </param>
    public async Task<int> ResubmitAsync(
        string targetEntityName,
        IReadOnlyList<MessageRecord> messages,
        bool removeFromDeadLetter,
        EntityPath? deadLetterSource = null,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
        {
            return 0;
        }

        var sendable = messages.Select(message => message.ToSendableMessage()).ToList();
        var sent = await SendAsync(targetEntityName, sendable, cancellationToken).ConfigureAwait(false);

        if (removeFromDeadLetter && deadLetterSource is not null)
        {
            await DeleteMessagesAsync(
                deadLetterSource,
                deadLetter: true,
                messages.Select(message => message.SequenceNumber).ToHashSet(),
                cancellationToken).ConfigureAwait(false);
        }

        return sent;
    }

    /// <summary>
    /// Deletes specific messages by sequence number.
    /// </summary>
    /// <remarks>
    /// Service Bus has no delete-by-sequence-number operation, so this receives messages
    /// under lock, completes the ones that match and abandons the rest. Abandoning
    /// increments the delivery count on the untargeted messages, which can push a message
    /// close to <c>MaxDeliveryCount</c> on an entity that is already near its limit.
    /// </remarks>
    /// <returns>How many of the requested messages were actually deleted.</returns>
    public async Task<int> DeleteMessagesAsync(
        EntityPath entity,
        bool deadLetter,
        IReadOnlySet<long> sequenceNumbers,
        CancellationToken cancellationToken = default)
    {
        if (sequenceNumbers.Count == 0)
        {
            return 0;
        }

        await using var receiver = CreateReceiver(entity, deadLetter, ServiceBusReceiveMode.PeekLock);

        var remaining = new HashSet<long>(sequenceNumbers);
        var deleted = 0;
        var emptyBatches = 0;

        // Stop once every target is accounted for, or the entity stops yielding messages.
        while (remaining.Count > 0 && emptyBatches < 2)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await receiver
                .ReceiveMessagesAsync(MaxBatchSize, BatchWaitTime, cancellationToken)
                .ConfigureAwait(false);

            if (batch is null || batch.Count == 0)
            {
                emptyBatches++;
                continue;
            }

            emptyBatches = 0;

            foreach (var message in batch)
            {
                if (remaining.Remove(message.SequenceNumber))
                {
                    await receiver.CompleteMessageAsync(message, cancellationToken).ConfigureAwait(false);
                    deleted++;
                }
                else
                {
                    await receiver.AbandonMessageAsync(message, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return deleted;
    }

    // ----------------------------------------------------------------- purge

    /// <summary>
    /// Removes every message from an entity or its dead-letter sub-queue.
    /// </summary>
    /// <remarks>
    /// Uses receive-and-delete in full batches, which is the fastest way to drain an
    /// entity over AMQP. Because the service can return an empty batch while messages
    /// remain, draining is only considered finished after several consecutive empty
    /// batches.
    /// </remarks>
    /// <returns>The number of messages deleted.</returns>
    public async Task<long> PurgeAsync(
        EntityPath entity,
        bool deadLetter,
        IProgress<PurgeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        const int emptyBatchesBeforeDone = 3;

        await using var receiver = CreateReceiver(entity, deadLetter, ServiceBusReceiveMode.ReceiveAndDelete);

        long deleted = 0;
        var emptyBatches = 0;

        while (emptyBatches < emptyBatchesBeforeDone)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await receiver
                .ReceiveMessagesAsync(MaxBatchSize, BatchWaitTime, cancellationToken)
                .ConfigureAwait(false);

            if (batch is null || batch.Count == 0)
            {
                emptyBatches++;
                continue;
            }

            emptyBatches = 0;
            deleted += batch.Count;
            progress?.Report(new PurgeProgress(deleted, entity.ToString(), deadLetter));
        }

        return deleted;
    }

    // ------------------------------------------------------------- internals

    ServiceBusReceiver CreateReceiver(EntityPath entity, bool deadLetter, ServiceBusReceiveMode receiveMode)
    {
        var options = new ServiceBusReceiverOptions
        {
            ReceiveMode = receiveMode,
            SubQueue = deadLetter ? SubQueue.DeadLetter : SubQueue.None,
            // Prefetch would pull messages the user never asked for and, in PeekLock,
            // start their lock timers ticking. Keep it off.
            PrefetchCount = 0
        };

        return entity.IsSubscription
            ? session.Messaging.CreateReceiver(entity.TopicOrQueue, entity.Subscription!, options)
            : session.Messaging.CreateReceiver(entity.TopicOrQueue, options);
    }
}
