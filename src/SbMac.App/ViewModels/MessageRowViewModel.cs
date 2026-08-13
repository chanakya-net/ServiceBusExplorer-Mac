using System.Text;

using SbMac.Core.Messaging;

namespace SbMac.App.ViewModels;

/// <summary>
/// One row in the message detail pane. <paramref name="IsSection"/> marks a group heading
/// rather than a value, so the list can render both from a single collection.
/// </summary>
public sealed record MessagePropertyEntry(string Name, string Value, bool IsSection);

/// <summary>
/// One row in the message grid. Wraps a <see cref="MessageRecord"/> with the
/// pre-formatted strings the grid columns bind to.
/// </summary>
public sealed class MessageRowViewModel : ViewModelBase
{
    public MessageRowViewModel(MessageRecord record)
    {
        Record = record;
    }

    public MessageRecord Record { get; }

    public long SequenceNumber => Record.SequenceNumber;

    public string MessageId => Record.MessageId;

    /// <summary>
    /// Local time — message timestamps are UTC on the wire, which reads badly in a grid.
    /// </summary>
    /// <remarks>
    /// The date is dropped for messages enqueued today, which is the overwhelmingly common
    /// case when peeking a live queue. It buys the Preview column roughly 60px of width and
    /// makes the times themselves easier to compare down the column.
    /// </remarks>
    public string EnqueuedTime
    {
        get
        {
            var local = Record.EnqueuedTime.ToLocalTime();
            return local.Date == DateTimeOffset.Now.Date
                ? local.ToString("HH:mm:ss")
                : local.ToString("MMM d, HH:mm:ss");
        }
    }

    public string Size => FormatBytes(Record.SizeInBytes);

    public int DeliveryCount => Record.DeliveryCount;

    /// <summary>
    /// The delivery count for the grid. Blank for events: Event Hubs never redelivers, so
    /// a column of zeroes would read as information it isn't.
    /// </summary>
    public string? DeliveryCountText => Record.IsEvent ? null : Record.DeliveryCount.ToString();

    /// <summary>The partition an event came from. Null for Service Bus messages.</summary>
    public string? PartitionId => Record.Event?.PartitionId;

    public string? Label => Record.Subject;

    public string? SessionId => Record.SessionId;

    public string? ContentType => Record.ContentType;

    public string? DeadLetterReason => Record.DeadLetterReason;

    /// <summary>
    /// A one-line summary of the body for the grid.
    /// </summary>
    /// <remarks>
    /// Every run of whitespace collapses to a single space rather than the preview being
    /// the literal first line. Bodies are pretty-printed for the detail pane, so a
    /// first-line preview of formatted JSON reads as just "{" on every row.
    /// </remarks>
    public string Preview
    {
        get
        {
            var text = Record.BodyView.Text;
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(Math.Min(text.Length, 200));
            var lastWasSpace = false;

            foreach (var character in text)
            {
                if (char.IsWhiteSpace(character))
                {
                    // Leading whitespace is dropped entirely; interior runs become one space.
                    if (builder.Length > 0 && !lastWasSpace)
                    {
                        builder.Append(' ');
                        lastWasSpace = true;
                    }

                    continue;
                }

                builder.Append(character);
                lastWasSpace = false;

                if (builder.Length >= 200)
                {
                    break;
                }
            }

            var preview = builder.ToString().TrimEnd();
            return preview.Length >= 200 ? preview[..160] + "…" : preview;
        }
    }

    /// <summary>The full detail pane text: every broker and user property, one per line.</summary>
    public string PropertiesText
    {
        get
        {
            var builder = new StringBuilder();

            void Append(string name, object? value)
            {
                if (value is null)
                {
                    return;
                }

                var text = value.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    builder.Append(name.PadRight(30)).Append(text).Append('\n');
                }
            }

            Append("MessageId", Record.MessageId);
            Append("SequenceNumber", Record.SequenceNumber);
            Append("EnqueuedSequenceNumber", Record.EnqueuedSequenceNumber == 0 ? null : Record.EnqueuedSequenceNumber);
            Append("EnqueuedTime", Record.EnqueuedTime.ToLocalTime().ToString("O"));

            if (Record.Event is { } origin)
            {
                Append("PartitionId", origin.PartitionId);
                Append("Offset", origin.Offset);
            }
            else
            {
                // Locks, expiry, redelivery and message state are broker behaviour Event
                // Hubs has no equivalent of, so they are left off rather than shown at
                // whatever default they happen to hold.
                Append("ExpiresAt", Record.ExpiresAt == DateTimeOffset.MaxValue
                    ? "never"
                    : Record.ExpiresAt.ToLocalTime().ToString("O"));

                if (Record.ScheduledEnqueueTime > DateTimeOffset.MinValue)
                {
                    Append("ScheduledEnqueueTime", Record.ScheduledEnqueueTime.ToLocalTime().ToString("O"));
                }

                Append("TimeToLive", Record.TimeToLive == TimeSpan.MaxValue ? "infinite" : Record.TimeToLive.ToString());
                Append("DeliveryCount", Record.DeliveryCount);
                Append("State", Record.State);
            }

            Append("Subject", Record.Subject);
            Append("CorrelationId", Record.CorrelationId);
            Append("SessionId", Record.SessionId);
            Append("PartitionKey", Record.PartitionKey);
            Append("To", Record.To);
            Append("ReplyTo", Record.ReplyTo);
            Append("ReplyToSessionId", Record.ReplyToSessionId);
            Append("ContentType", Record.ContentType);
            Append("Size", FormatBytes(Record.SizeInBytes));
            Append("BodyFormat", Record.BodyView.FormatLabel);

            Append("DeadLetterReason", Record.DeadLetterReason);
            Append("DeadLetterErrorDescription", Record.DeadLetterErrorDescription);
            Append("DeadLetterSource", Record.DeadLetterSource);

            if (Record.ApplicationProperties.Count > 0)
            {
                builder.Append("\n── Application properties ──\n");
                foreach (var pair in Record.ApplicationProperties)
                {
                    Append(pair.Key, pair.Value ?? "(null)");
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// The same information as <see cref="PropertiesText"/>, but structured, so the detail
    /// pane can lay it out as a real two-column list instead of space-padded monospace.
    /// </summary>
    public IReadOnlyList<MessagePropertyEntry> PropertyEntries
    {
        get
        {
            var entries = new List<MessagePropertyEntry>(24);

            void Add(string name, object? value)
            {
                var text = value?.ToString();
                if (!string.IsNullOrEmpty(text))
                {
                    entries.Add(new MessagePropertyEntry(name, text, IsSection: false));
                }
            }

            entries.Add(new MessagePropertyEntry("Identity", string.Empty, IsSection: true));
            Add("Message ID", Record.MessageId);
            Add("Sequence number", Record.SequenceNumber);
            Add("Correlation ID", Record.CorrelationId);
            Add("Session ID", Record.SessionId);
            Add("Partition key", Record.PartitionKey);

            if (Record.Event is { } origin)
            {
                entries.Add(new MessagePropertyEntry("Position", string.Empty, IsSection: true));
                Add("Partition", origin.PartitionId);
                Add("Offset", origin.Offset);
                Add("Enqueued", Record.EnqueuedTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"));
            }
            else
            {
                entries.Add(new MessagePropertyEntry("Delivery", string.Empty, IsSection: true));
                Add("Enqueued", Record.EnqueuedTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"));
                Add("Expires", Record.ExpiresAt == DateTimeOffset.MaxValue
                    ? "never"
                    : Record.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                Add("Time to live", Record.TimeToLive == TimeSpan.MaxValue ? "infinite" : Record.TimeToLive.ToString());
                Add("Delivery count", Record.DeliveryCount);
                Add("State", Record.State);

                if (Record.ScheduledEnqueueTime > DateTimeOffset.MinValue)
                {
                    Add("Scheduled for", Record.ScheduledEnqueueTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }

            entries.Add(new MessagePropertyEntry("Content", string.Empty, IsSection: true));
            Add("Label", Record.Subject);
            Add("Content type", Record.ContentType);
            Add("Body format", Record.BodyView.FormatLabel);
            Add("Size", FormatBytes(Record.SizeInBytes));
            Add("To", Record.To);
            Add("Reply to", Record.ReplyTo);
            Add("Reply to session", Record.ReplyToSessionId);

            if (Record.DeadLetterReason is not null || Record.DeadLetterSource is not null)
            {
                entries.Add(new MessagePropertyEntry("Dead-letter", string.Empty, IsSection: true));
                Add("Reason", Record.DeadLetterReason);
                Add("Description", Record.DeadLetterErrorDescription);
                Add("Source", Record.DeadLetterSource);
            }

            if (Record.ApplicationProperties.Count > 0)
            {
                entries.Add(new MessagePropertyEntry("Application properties", string.Empty, IsSection: true));
                foreach (var pair in Record.ApplicationProperties)
                {
                    Add(pair.Key, pair.Value ?? "(null)");
                }
            }

            return entries;
        }
    }

    public string BodyText => Record.BodyView.Text;

    public string BodyFormatLabel => Record.BodyView.FormatLabel;

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.##} MB"
    };
}
