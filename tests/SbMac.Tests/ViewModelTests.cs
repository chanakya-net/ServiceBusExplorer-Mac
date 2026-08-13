using System.Text;

using Azure.Messaging.ServiceBus;

using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Dialogs;

using SbMac.Core.Entities;
using SbMac.Core.Messaging;

using Xunit;

namespace SbMac.Tests;

public class EntityPathTests
{
    [Fact]
    public void QueuePathIsJustTheName()
    {
        var path = EntityPath.ForQueue("orders");

        Assert.False(path.IsSubscription);
        Assert.Equal("orders", path.ToString());
    }

    [Fact]
    public void SubscriptionPathUsesTheServiceBusForm()
    {
        var path = EntityPath.ForSubscription("order-events", "audit");

        Assert.True(path.IsSubscription);
        Assert.Equal("order-events/Subscriptions/audit", path.ToString());
    }
}

public class SendMessageViewModelTests
{
    [Fact]
    public void BuildProducesOneMessagePerCopy()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders"))
        {
            Body = "hello",
            Copies = 3,
            NumberCopies = false
        };

        var result = viewModel.Build();

        Assert.NotNull(result);
        Assert.Equal(3, result.Messages.Count);
        Assert.Equal("orders", result.TargetName);
    }

    [Fact]
    public void CopyNumberTokenIsSubstitutedWhenNumberingIsOn()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders"))
        {
            Body = """{"seq":{n}}""",
            Copies = 2,
            NumberCopies = true
        };

        var result = viewModel.Build();

        Assert.NotNull(result);
        Assert.Equal("""{"seq":1}""", Encoding.UTF8.GetString(result.Messages[0].Body));
        Assert.Equal("""{"seq":2}""", Encoding.UTF8.GetString(result.Messages[1].Body));
    }

    [Fact]
    public void ApplicationPropertiesAreCarriedOntoTheMessage()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders")) { Body = "hello" };
        viewModel.Properties.Add(new PropertyRowViewModel { Key = "region", Value = "emea" });

        // A blank key would create an unusable property, so it must be dropped.
        viewModel.Properties.Add(new PropertyRowViewModel { Key = "  ", Value = "ignored" });

        var result = viewModel.Build();

        var message = Assert.Single(result!.Messages);
        Assert.Equal("emea", message.ApplicationProperties["region"]);
        Assert.Single(message.ApplicationProperties);
    }

    /// <summary>
    /// Service Bus derives the partition key from the session id on a session-enabled
    /// entity and rejects a conflicting explicit value.
    /// </summary>
    [Fact]
    public void PartitionKeyIsNotSetWhenASessionIdIsPresent()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders"))
        {
            Body = "hello",
            SessionId = "session-1",
            PartitionKey = "partition-1"
        };

        var message = Assert.Single(viewModel.Build()!.Messages);

        Assert.Equal("session-1", message.SessionId);
        Assert.Null(message.PartitionKey);
    }

    [Fact]
    public void PartitionKeyIsSetWhenThereIsNoSession()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders"))
        {
            Body = "hello",
            PartitionKey = "partition-1"
        };

        var message = Assert.Single(viewModel.Build()!.Messages);

        Assert.Equal("partition-1", message.PartitionKey);
    }

    [Fact]
    public void SchedulingInThePastIsRejectedWithAMessage()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders"))
        {
            Body = "hello",
            Schedule = true,
            ScheduledDate = DateTimeOffset.Now.AddDays(-1),
            ScheduledTime = TimeSpan.FromHours(9)
        };

        Assert.Null(viewModel.Build());
        Assert.NotNull(viewModel.ValidationError);
    }

    [Fact]
    public void SchedulingInTheFutureIsAccepted()
    {
        var when = DateTimeOffset.Now.AddDays(1);

        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders"))
        {
            Body = "hello",
            Schedule = true,
            ScheduledDate = when,
            ScheduledTime = new TimeSpan(9, 30, 0)
        };

        var result = viewModel.Build();

        Assert.NotNull(result);
        Assert.NotNull(result.ScheduledEnqueueTime);
        Assert.Equal(9, result.ScheduledEnqueueTime.Value.Hour);
    }

    [Fact]
    public void SeedingFromAMessagePrefillsTheForm()
    {
        var seed = new MessageRecord
        {
            Body = BinaryData.FromString("""{"orderId":7}"""),
            SourceEntityPath = "orders/$DeadLetterQueue",
            MessageId = "abc",
            ContentType = "application/json",
            Subject = "order-created",
            CorrelationId = "corr-1",
            ApplicationProperties = new Dictionary<string, object?> { ["region"] = "emea" },
            IsDeadLetter = true
        };

        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders", seed));

        Assert.Contains("orderId", viewModel.Body);
        Assert.Equal("application/json", viewModel.ContentType);
        Assert.Equal("order-created", viewModel.Subject);
        Assert.Equal("corr-1", viewModel.CorrelationId);
        Assert.Equal("region", Assert.Single(viewModel.Properties).Key);
    }

    [Fact]
    public void CannotSendAnEmptyBody()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders")) { Body = string.Empty };

        Assert.False(viewModel.CanSend);
    }
}

public class MessageRecordTests
{
    static MessageRecord BuildRecord() => new()
    {
        Body = BinaryData.FromString("payload"),
        SourceEntityPath = "orders",
        MessageId = "abc",
        CorrelationId = "corr-1",
        Subject = "order-created",
        ContentType = "application/json",
        SessionId = "session-1",
        TimeToLive = TimeSpan.FromMinutes(10),
        ApplicationProperties = new Dictionary<string, object?> { ["region"] = "emea" }
    };

    [Fact]
    public void ResubmitCopiesUserPropertiesButNotBrokerAssignedOnes()
    {
        var message = BuildRecord().ToSendableMessage();

        Assert.Equal("payload", Encoding.UTF8.GetString(message.Body));
        Assert.Equal("corr-1", message.CorrelationId);
        Assert.Equal("order-created", message.Subject);
        Assert.Equal("emea", message.ApplicationProperties["region"]);

        // Reusing the original id would defeat duplicate detection on a resubmit.
        Assert.NotEqual("abc", message.MessageId);
    }

    [Fact]
    public void ResubmitCanPreserveTheMessageIdOnRequest()
    {
        var message = BuildRecord().ToSendableMessage(preserveMessageId: true);

        Assert.Equal("abc", message.MessageId);
    }

    /// <summary>
    /// TimeSpan.MaxValue is the "never expires" sentinel, and it is also a fresh
    /// message's default, so a resubmit must leave the property untouched rather than
    /// assigning the sentinel back.
    /// </summary>
    [Fact]
    public void InfiniteTimeToLiveLeavesTheResubmittedMessageAtItsDefault()
    {
        var record = new MessageRecord
        {
            Body = BinaryData.FromString("payload"),
            SourceEntityPath = "orders",
            TimeToLive = TimeSpan.MaxValue
        };

        var message = record.ToSendableMessage();

        Assert.Equal(new ServiceBusMessage().TimeToLive, message.TimeToLive);
    }

    /// <summary>
    /// A session message reports the same value for PartitionKey and SessionId. Copying
    /// both across throws, so resubmitting any session message would fail outright.
    /// </summary>
    [Fact]
    public void SessionMessagesResubmitWithoutAPartitionKeyConflict()
    {
        var record = new MessageRecord
        {
            Body = BinaryData.FromString("payload"),
            SourceEntityPath = "orders",
            SessionId = "session-1",
            PartitionKey = "session-1"
        };

        var message = record.ToSendableMessage();

        Assert.Equal("session-1", message.SessionId);
    }

    [Fact]
    public void PartitionKeyIsCopiedWhenThereIsNoSession()
    {
        var record = new MessageRecord
        {
            Body = BinaryData.FromString("payload"),
            SourceEntityPath = "orders",
            PartitionKey = "partition-1"
        };

        Assert.Equal("partition-1", record.ToSendableMessage().PartitionKey);
    }

    [Fact]
    public void FiniteTimeToLiveIsCopied()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), BuildRecord().ToSendableMessage().TimeToLive);
    }
}

public class MessageRowViewModelTests
{
    static MessageRowViewModel BuildRow(string body) => new(new MessageRecord
    {
        Body = BinaryData.FromString(body),
        SourceEntityPath = "orders",
        MessageId = "abc",
        SequenceNumber = 42,
        EnqueuedTime = DateTimeOffset.UtcNow,
        DeadLetterReason = "MaxDeliveryCountExceeded"
    });

    /// <summary>
    /// Bodies are pretty-printed for the detail pane, so a first-line preview of formatted
    /// JSON is just "{" on every row. Collapsing whitespace keeps the preview informative.
    /// </summary>
    [Fact]
    public void PreviewCollapsesTheBodyOntoOneLine()
    {
        Assert.Equal("first line second line", BuildRow("first line\nsecond line").Preview);
    }

    [Fact]
    public void PreviewOfPrettyPrintedJsonShowsTheContent()
    {
        var preview = BuildRow("{\n  \"orderId\": 40921,\n  \"region\": \"emea\"\n}").Preview;

        Assert.StartsWith("{ \"orderId\": 40921,", preview);
        Assert.Contains("emea", preview);
    }

    [Fact]
    public void PreviewTrimsLeadingAndTrailingWhitespace()
    {
        Assert.Equal("payload", BuildRow("\n\n   payload   \n").Preview);
    }

    [Fact]
    public void PreviewIsTruncatedForLongSingleLineBodies()
    {
        var preview = BuildRow(new string('x', 500)).Preview;

        Assert.EndsWith("…", preview);
        Assert.True(preview.Length <= 161);
    }

    [Fact]
    public void PropertiesTextIncludesTheDeadLetterReason()
    {
        var text = BuildRow("payload").PropertiesText;

        Assert.Contains("MaxDeliveryCountExceeded", text);
        Assert.Contains("SequenceNumber", text);
        Assert.Contains("42", text);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(3145728, "3 MB")]
    public void ByteSizesAreFormattedForReading(long bytes, string expected)
    {
        Assert.Equal(expected, MessageRowViewModel.FormatBytes(bytes));
    }
}

public class DurationTextTests
{
    [Theory]
    [InlineData("PT30S", "00:00:30")]
    [InlineData("PT1H", "01:00:00")]
    [InlineData("P1D", "1.00:00:00")]
    public void Iso8601DurationsAreShownInAReadableForm(string iso, string expected)
    {
        Assert.Equal(expected, DurationText.ToDisplay(iso));
    }

    [Theory]
    [InlineData("00:00:30", "PT30S")]
    [InlineData("1.00:00:00", "P1D")]
    public void ReadableDurationsAreStoredAsIso8601(string display, string expected)
    {
        Assert.Equal(expected, DurationText.ToDefinition(display));
    }

    [Fact]
    public void BlankRoundTripsToNullRatherThanZero()
    {
        // Null means "leave the service default alone"; zero would be sent as a real value.
        Assert.Null(DurationText.ToDefinition("   "));
        Assert.Equal(string.Empty, DurationText.ToDisplay(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("00:00:30")]
    [InlineData("PT30S")]
    public void ValidInputIsAccepted(string value) => Assert.True(DurationText.IsValid(value));

    [Theory]
    [InlineData("thirty seconds")]
    [InlineData("PT")]
    [InlineData("99:99")]
    public void InvalidInputIsRejected(string value) => Assert.False(DurationText.IsValid(value));
}
