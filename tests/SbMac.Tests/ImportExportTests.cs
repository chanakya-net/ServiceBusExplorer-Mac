using Azure.Messaging.ServiceBus.Administration;

using SbMac.Core.ImportExport;

using Xunit;

namespace SbMac.Tests;

public class ImportExportSerializationTests
{
    static NamespaceDefinition BuildSample() => new()
    {
        SourceNamespace = "contoso.servicebus.windows.net",
        Queues =
        {
            new QueueDefinition
            {
                Name = "orders",
                MaxSizeInMegabytes = 2048,
                MaxDeliveryCount = 5,
                LockDuration = "PT45S",
                RequiresSession = true,
                DeadLetteringOnMessageExpiration = true,
                ForwardTo = "archive"
            }
        },
        Topics =
        {
            new TopicDefinition
            {
                Name = "order-events",
                SupportOrdering = true,
                Subscriptions =
                {
                    new SubscriptionDefinition
                    {
                        Name = "audit",
                        MaxDeliveryCount = 3,
                        Rules =
                        {
                            new RuleDefinition
                            {
                                Name = "emea-only",
                                FilterType = "Sql",
                                SqlExpression = "region = 'emea'",
                                SqlAction = "SET routed = 'yes'"
                            }
                        }
                    }
                }
            }
        }
    };

    [Theory]
    [InlineData(DefinitionFormat.Xml)]
    [InlineData(DefinitionFormat.Json)]
    public void DefinitionsRoundTripThroughBothFormats(DefinitionFormat format)
    {
        var original = BuildSample();

        var serialized = ImportExportService.Serialize(original, format);
        var restored = ImportExportService.Deserialize(serialized, format);

        var queue = Assert.Single(restored.Queues);
        Assert.Equal("orders", queue.Name);
        Assert.Equal(2048, queue.MaxSizeInMegabytes);
        Assert.Equal(5, queue.MaxDeliveryCount);
        Assert.Equal("PT45S", queue.LockDuration);
        Assert.True(queue.RequiresSession);
        Assert.Equal("archive", queue.ForwardTo);

        var topic = Assert.Single(restored.Topics);
        var subscription = Assert.Single(topic.Subscriptions);
        var rule = Assert.Single(subscription.Rules);

        Assert.Equal("audit", subscription.Name);
        Assert.Equal("emea-only", rule.Name);
        Assert.Equal("region = 'emea'", rule.SqlExpression);
        Assert.Equal("SET routed = 'yes'", rule.SqlAction);
    }

    [Fact]
    public void ExportedXmlNamesTheSourceNamespace()
    {
        var xml = ImportExportService.Serialize(BuildSample(), DefinitionFormat.Xml);

        Assert.Contains("contoso.servicebus.windows.net", xml);
        Assert.Contains("<Queue", xml);
        Assert.Contains("<Topic", xml);
    }
}

public class DefinitionToOptionsTests
{
    [Fact]
    public void QueueDurationsAreConvertedFromIso8601()
    {
        var options = ImportExportService.ToCreateOptions(new QueueDefinition
        {
            Name = "orders",
            LockDuration = "PT45S",
            DefaultMessageTimeToLive = "P1D",
            AutoDeleteOnIdle = "PT1H"
        });

        Assert.Equal(TimeSpan.FromSeconds(45), options.LockDuration);
        Assert.Equal(TimeSpan.FromDays(1), options.DefaultMessageTimeToLive);
        Assert.Equal(TimeSpan.FromHours(1), options.AutoDeleteOnIdle);
    }

    /// <summary>
    /// Service Bus rejects a duplicate-detection window on a queue that doesn't have
    /// duplicate detection turned on, so the window must not be carried across.
    /// </summary>
    [Fact]
    public void DuplicateDetectionWindowIsDroppedWhenDetectionIsOff()
    {
        var options = ImportExportService.ToCreateOptions(new QueueDefinition
        {
            Name = "orders",
            RequiresDuplicateDetection = false,
            DuplicateDetectionHistoryTimeWindow = "PT20M"
        });

        // The SDK's own default is 1 minute; anything else would mean we sent the value.
        Assert.NotEqual(TimeSpan.FromMinutes(20), options.DuplicateDetectionHistoryTimeWindow);
    }

    [Fact]
    public void DuplicateDetectionWindowIsKeptWhenDetectionIsOn()
    {
        var options = ImportExportService.ToCreateOptions(new QueueDefinition
        {
            Name = "orders",
            RequiresDuplicateDetection = true,
            DuplicateDetectionHistoryTimeWindow = "PT20M"
        });

        Assert.Equal(TimeSpan.FromMinutes(20), options.DuplicateDetectionHistoryTimeWindow);
    }

    /// <summary>
    /// A blank forwarding target must stay unset. Sending an empty string makes the
    /// service look for an entity with an empty name and fail the whole create.
    /// </summary>
    [Fact]
    public void BlankForwardingTargetsAreLeftUnset()
    {
        var options = ImportExportService.ToCreateOptions(new QueueDefinition
        {
            Name = "orders",
            ForwardTo = "   ",
            ForwardDeadLetteredMessagesTo = ""
        });

        Assert.True(string.IsNullOrEmpty(options.ForwardTo));
        Assert.True(string.IsNullOrEmpty(options.ForwardDeadLetteredMessagesTo));
    }

    [Fact]
    public void DisabledStatusIsCarriedThrough()
    {
        var options = ImportExportService.ToCreateOptions(new QueueDefinition { Name = "orders", Status = "Disabled" });

        Assert.Equal(EntityStatus.Disabled, options.Status);
    }

    [Fact]
    public void UnrecognisedStatusFallsBackToActive()
    {
        var options = ImportExportService.ToCreateOptions(new QueueDefinition { Name = "orders", Status = "Bananas" });

        Assert.Equal(EntityStatus.Active, options.Status);
    }

    [Theory]
    [InlineData("Sql", typeof(SqlRuleFilter))]
    [InlineData("Correlation", typeof(CorrelationRuleFilter))]
    [InlineData("True", typeof(TrueRuleFilter))]
    [InlineData("False", typeof(FalseRuleFilter))]
    public void RuleFilterTypesAreMappedToTheRightSdkFilter(string filterType, Type expected)
    {
        var options = ImportExportService.ToCreateOptions(new RuleDefinition
        {
            Name = "rule",
            FilterType = filterType,
            SqlExpression = "region = 'emea'"
        });

        Assert.IsType(expected, options.Filter);
    }

    /// <summary>
    /// TrueRuleFilter and FalseRuleFilter derive from SqlRuleFilter, so a naive type
    /// switch reports them as plain SQL filters and the round-trip silently changes them.
    /// </summary>
    [Theory]
    [InlineData("True")]
    [InlineData("False")]
    public void TrueAndFalseFiltersSurviveARoundTrip(string filterType)
    {
        var options = ImportExportService.ToCreateOptions(new RuleDefinition
        {
            Name = "rule",
            FilterType = filterType
        });

        var definition = ImportExportService.ToDefinition("rule", options.Filter, options.Action);

        Assert.Equal(filterType, definition.FilterType);
    }

    [Fact]
    public void SqlFilterSurvivesARoundTrip()
    {
        var options = ImportExportService.ToCreateOptions(new RuleDefinition
        {
            Name = "rule",
            FilterType = "Sql",
            SqlExpression = "priority > 3"
        });

        var definition = ImportExportService.ToDefinition("rule", options.Filter, options.Action);

        Assert.Equal("Sql", definition.FilterType);
        Assert.Equal("priority > 3", definition.SqlExpression);
    }

    [Fact]
    public void CorrelationFilterFieldsSurviveARoundTrip()
    {
        var options = ImportExportService.ToCreateOptions(new RuleDefinition
        {
            Name = "rule",
            FilterType = "Correlation",
            CorrelationId = "abc",
            Subject = "order-created",
            ContentType = "application/json"
        });

        var definition = ImportExportService.ToDefinition("rule", options.Filter, options.Action);

        Assert.Equal("Correlation", definition.FilterType);
        Assert.Equal("abc", definition.CorrelationId);
        Assert.Equal("order-created", definition.Subject);
        Assert.Equal("application/json", definition.ContentType);
    }

    [Fact]
    public void AnEmptySqlExpressionBecomesMatchAll()
    {
        var options = ImportExportService.ToCreateOptions(new RuleDefinition { Name = "rule", FilterType = "Sql" });

        Assert.Equal("1=1", Assert.IsType<SqlRuleFilter>(options.Filter).SqlExpression);
    }

    [Fact]
    public void SubscriptionOptionsCarryTheTopicName()
    {
        var options = ImportExportService.ToCreateOptions("order-events", new SubscriptionDefinition
        {
            Name = "audit",
            MaxDeliveryCount = 3,
            LockDuration = "PT1M"
        });

        Assert.Equal("order-events", options.TopicName);
        Assert.Equal("audit", options.SubscriptionName);
        Assert.Equal(3, options.MaxDeliveryCount);
        Assert.Equal(TimeSpan.FromMinutes(1), options.LockDuration);
    }
}
