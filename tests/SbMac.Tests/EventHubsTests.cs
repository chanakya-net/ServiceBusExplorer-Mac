using Azure.Messaging.EventHubs;
using Azure.Messaging.ServiceBus;

using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Dialogs;
using SbMac.App.ViewModels.Tree;

using SbMac.Core.Connections;
using SbMac.Core.EventHubs;
using SbMac.Core.Messaging;

using Xunit;

namespace SbMac.Tests;

/// <summary>Shared fixtures so each test only spells out what it is actually asserting.</summary>
static class EventHubFixtures
{
    public const string NamespaceConnectionString =
        "Endpoint=sb://contoso.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;" +
        "SharedAccessKey=abc123def456==";

    public const string HubScopedConnectionString = NamespaceConnectionString + ";EntityPath=telemetry";

    public static NamespaceConnection Connection(string? connectionString = null) => new()
    {
        Name = "contoso",
        Kind = NamespaceKind.EventHubs,
        AuthenticationMode = AuthenticationMode.ConnectionString,
        ConnectionString = connectionString ?? NamespaceConnectionString
    };

    public static EventHubEntity Hub(
        string name = "telemetry",
        IReadOnlyList<string>? partitions = null,
        IReadOnlyList<string>? consumerGroups = null) =>
        new(name, DateTimeOffset.UtcNow, partitions ?? ["0", "1"], consumerGroups ?? []);

    public static PartitionEntity Partition(
        string partitionId = "0",
        long beginning = 10,
        long last = 20,
        bool isEmpty = false) =>
        new("telemetry", partitionId, beginning, last, DateTimeOffset.UtcNow, isEmpty);

    /// <summary>Builds a selectable tree without connecting to anything.</summary>
    public static (NamespaceNodeViewModel Namespace, EventHubNodeViewModel Hub, PartitionNodeViewModel Partition)
        Tree(EventHubEntity? hub = null)
    {
        var namespaceNode = new NamespaceNodeViewModel(Connection());
        var folder = new EventHubFolderNodeViewModel { Parent = namespaceNode };
        var hubNode = new EventHubNodeViewModel(hub ?? Hub()) { Parent = folder };
        var partitionNode = new PartitionNodeViewModel(Partition()) { Parent = hubNode };

        return (namespaceNode, hubNode, partitionNode);
    }
}

public class EventHubsConnectionTests
{
    [Fact]
    public void SavedNamespacesDefaultToServiceBus()
    {
        // Connections written before Event Hubs support existed have no kind in their JSON.
        Assert.Equal(NamespaceKind.ServiceBus, new NamespaceConnection().Kind);
    }

    [Fact]
    public void HubScopedConnectionStringContributesItsEntityPath()
    {
        var connection = EventHubFixtures.Connection(EventHubFixtures.HubScopedConnectionString);

        Assert.Equal(["telemetry"], connection.ConfiguredEventHubNames);
    }

    [Fact]
    public void ConfiguredHubsMergeTheEntityPathWithTheTypedNames()
    {
        var connection = EventHubFixtures.Connection(EventHubFixtures.HubScopedConnectionString);
        connection.EventHubNames = ["audit", "TELEMETRY"];

        // The entity path and the typed name are the same hub in different casing, and
        // Event Hubs names are case-insensitive, so it must appear once.
        Assert.Equal(["audit", "telemetry"], connection.ConfiguredEventHubNames);
    }

    [Fact]
    public void ConfiguredHubsIgnoreBlankEntries()
    {
        var connection = EventHubFixtures.Connection();
        connection.EventHubNames = ["  ", "audit", string.Empty];

        Assert.Equal(["audit"], connection.ConfiguredEventHubNames);
    }

    [Fact]
    public void ConsumerGroupFallsBackToTheServiceDefault()
    {
        Assert.Equal("$Default", EventHubFixtures.Connection().EffectiveConsumerGroup);
        Assert.Equal("$Default", new NamespaceConnection { ConsumerGroup = "   " }.EffectiveConsumerGroup);
        Assert.Equal("analytics", new NamespaceConnection { ConsumerGroup = " analytics " }.EffectiveConsumerGroup);
    }

    /// <summary>
    /// The clone is handed to the editor while the original stays live in the tree, so a
    /// shared list instance would let an abandoned edit reach the connected namespace.
    /// </summary>
    [Fact]
    public void CloningACopiesTheHubListRatherThanSharingIt()
    {
        var original = EventHubFixtures.Connection();
        original.EventHubNames = ["telemetry"];

        var clone = original.Clone();
        clone.EventHubNames.Add("audit");

        Assert.Equal(["telemetry"], original.EventHubNames);
        Assert.Equal(["telemetry", "audit"], clone.EventHubNames);
    }
}

public class EventHubsSessionTests
{
    [Fact]
    public void CreatingASessionWithoutCredentialsFailsWithAClearMessage()
    {
        var connection = EventHubFixtures.Connection();
        connection.ConnectionString = null;

        var exception = Assert.Throws<InvalidOperationException>(() => EventHubsSession.Create(connection));
        Assert.Contains("connection string", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EntraSessionWithoutANamespaceFailsWithAClearMessage()
    {
        var connection = new NamespaceConnection
        {
            Kind = NamespaceKind.EventHubs,
            AuthenticationMode = AuthenticationMode.EntraId,
            FullyQualifiedNamespace = "   "
        };

        var exception = Assert.Throws<InvalidOperationException>(() => EventHubsSession.Create(connection));
        Assert.Contains("fully qualified namespace", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ANamespaceConnectionStringIsNotPinnedToAnyHub()
    {
        await using var session = EventHubsSession.Create(EventHubFixtures.Connection());

        Assert.Null(session.PinnedEventHub);
        Assert.Equal("contoso.servicebus.windows.net", session.Namespace);
        Assert.Equal("$Default", session.ConsumerGroup);
    }

    /// <summary>
    /// A connection string copied from a hub blade rather than the namespace carries an
    /// EntityPath. The SDK rejects being handed a hub name on top of one, so the session
    /// has to build its clients differently — this covers that both ways round.
    /// </summary>
    [Fact]
    public async Task AHubScopedConnectionStringBuildsClientsForItsOwnHub()
    {
        var connection = EventHubFixtures.Connection(EventHubFixtures.HubScopedConnectionString);
        await using var session = EventHubsSession.Create(connection);

        Assert.Equal("telemetry", session.PinnedEventHub);
        Assert.Equal("telemetry", session.Producer("telemetry").EventHubName);
        Assert.Equal("telemetry", session.Consumer("telemetry").EventHubName);
    }

    [Fact]
    public async Task AHubScopedConnectionStringCannotReachAnotherHub()
    {
        var connection = EventHubFixtures.Connection(EventHubFixtures.HubScopedConnectionString);
        await using var session = EventHubsSession.Create(connection);

        var exception = Assert.Throws<InvalidOperationException>(() => session.Producer("audit"));
        Assert.Contains("telemetry", exception.Message);
        Assert.Contains("audit", exception.Message);
    }

    [Fact]
    public async Task ClientsAreCreatedOncePerHubAndReused()
    {
        await using var session = EventHubsSession.Create(EventHubFixtures.Connection());

        Assert.Same(session.Producer("telemetry"), session.Producer("telemetry"));
        Assert.Same(session.Consumer("telemetry"), session.Consumer("telemetry"));
        Assert.NotSame(session.Producer("telemetry"), session.Producer("audit"));
    }

    [Fact]
    public async Task WebSocketTransportIsAccepted()
    {
        var connection = EventHubFixtures.Connection();
        connection.Transport = ServiceBusTransport.AmqpWebSockets;

        await using var session = EventHubsSession.Create(connection);

        Assert.Equal("telemetry", session.Consumer("telemetry").EventHubName);
    }

    [Fact]
    public async Task AskingADisposedSessionForAClientFailsLoudly()
    {
        var session = EventHubsSession.Create(EventHubFixtures.Connection());
        await session.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => session.Producer("telemetry"));
    }

    [Fact]
    public async Task TestingAConnectionWithNoKnownHubsDoesNothing()
    {
        // Nothing namespace-wide exists to call over the data plane, so this must not
        // reach the network — discovery is what reports a bad namespace instead.
        await using var session = EventHubsSession.Create(EventHubFixtures.Connection());

        await session.TestConnectionAsync(TestContext.Current.CancellationToken);
    }
}

public class EventMapperTests
{
    static EventData BuildEvent() => EventHubsModelFactory.EventData(
        eventBody: BinaryData.FromString("""{"deviceId":"probe-7"}"""),
        properties: new Dictionary<string, object> { ["region"] = "emea" },
        systemProperties: new Dictionary<string, object>(),
        partitionKey: "probe-7",
        sequenceNumber: 4021,
        offsetString: "112233",
        enqueuedTime: new DateTimeOffset(2026, 8, 13, 9, 30, 0, TimeSpan.Zero));

    [Fact]
    public void AnEventBecomesARecordCarryingItsPosition()
    {
        var record = EventMapper.ToRecord(BuildEvent(), "telemetry", "3");

        Assert.True(record.IsEvent);
        Assert.Equal("3", record.Event!.PartitionId);
        Assert.Equal("112233", record.Event.Offset);
        Assert.Equal(4021, record.SequenceNumber);
        Assert.Equal("telemetry", record.SourceEntityPath);
        Assert.Equal("probe-7", record.PartitionKey);
        Assert.Equal("emea", record.ApplicationProperties["region"]);
        Assert.Contains("probe-7", record.BodyView.Text);
    }

    [Fact]
    public void AServiceBusMessageIsARecordWithNoEventPosition()
    {
        var record = new MessageRecord { Body = BinaryData.FromString("x"), SourceEntityPath = "orders" };

        Assert.False(record.IsEvent);
        Assert.Null(record.Event);
    }

    [Fact]
    public void ComposedMessagesCarryTheirContentAcrossToAnEvent()
    {
        var message = new ServiceBusMessage(BinaryData.FromString("payload"))
        {
            MessageId = "abc",
            ContentType = "application/json",
            CorrelationId = "corr-1"
        };
        message.ApplicationProperties["region"] = "emea";

        var data = EventMapper.ToEventData(message);

        Assert.Equal("payload", data.EventBody.ToString());
        Assert.Equal("abc", data.MessageId);
        Assert.Equal("application/json", data.ContentType);
        Assert.Equal("corr-1", data.CorrelationId);
        Assert.Equal("emea", data.Properties["region"]);
    }

    /// <summary>
    /// The compose dialog's "label" is a Service Bus broker property. Dropping it silently
    /// would lose whatever the operator typed, so it is carried as a user property.
    /// </summary>
    [Fact]
    public void TheLabelSurvivesAsAUserProperty()
    {
        var message = new ServiceBusMessage(BinaryData.FromString("payload")) { Subject = "device-telemetry" };

        Assert.Equal("device-telemetry", EventMapper.ToEventData(message).Properties["Subject"]);
    }

    [Fact]
    public void NoLabelMeansNoSubjectProperty()
    {
        var data = EventMapper.ToEventData(new ServiceBusMessage(BinaryData.FromString("payload")));

        Assert.DoesNotContain("Subject", data.Properties.Keys);
    }

    /// <summary>
    /// The partition key belongs to the batch on the Event Hubs side, so it must not be
    /// smuggled onto the event — <see cref="EventHubService.SendAsync"/> takes it instead.
    /// </summary>
    [Fact]
    public void ThePartitionKeyIsNotCopiedOntoTheEvent()
    {
        var message = new ServiceBusMessage(BinaryData.FromString("payload")) { PartitionKey = "probe-7" };

        Assert.Null(EventMapper.ToEventData(message).PartitionKey);
    }
}

public class EventHubReadWindowTests
{
    [Theory]
    [InlineData(100, 4, 25)]
    [InlineData(100, 3, 34)]   // rounded up, so a request never comes back short
    [InlineData(1, 4, 1)]      // never zero, or a partition would be skipped entirely
    [InlineData(100, 0, 0)]
    public void TheFetchBudgetIsSharedBetweenPartitions(int count, int partitions, int expected)
    {
        Assert.Equal(expected, EventHubService.PerPartitionBudget(count, partitions));
    }

    /// <summary>
    /// The read window is inclusive at both ends, so covering N events starts N-1 back.
    /// Starting one further along would silently return N-1 events.
    /// </summary>
    [Fact]
    public void TheReadWindowCoversExactlyTheRequestedCount()
    {
        Assert.Equal(91, EventHubService.StartSequenceNumber(1, 100, 10));
    }

    /// <summary>
    /// Retention moves the beginning forward. Asking for a sequence number that has aged
    /// out is rejected outright, so the window has to be clamped rather than trusted.
    /// </summary>
    [Fact]
    public void TheReadWindowNeverStartsBeforeWhatIsStillRetained()
    {
        Assert.Equal(950, EventHubService.StartSequenceNumber(950, 1000, 500));
    }

    [Fact]
    public void AskingForMoreThanOnePartitionHoldsStartsAtItsBeginning()
    {
        Assert.Equal(0, EventHubService.StartSequenceNumber(0, 4, 100));
    }
}

public class EventHubEntityTests
{
    [Fact]
    public void PartitionPropertiesMapOntoTheTreesShape()
    {
        var properties = EventHubsModelFactory.PartitionProperties(
            eventHubName: "telemetry",
            partitionId: "3",
            isEmpty: false,
            beginningSequenceNumber: 10,
            lastSequenceNumber: 20,
            lastOffsetString: "99",
            lastEnqueuedTime: new DateTimeOffset(2026, 8, 13, 9, 30, 0, TimeSpan.Zero));

        var entity = EventMapper.ToEntity("telemetry", properties);

        Assert.Equal("telemetry", entity.EventHubName);
        Assert.Equal("3", entity.Id);
        Assert.Equal(10, entity.BeginningSequenceNumber);
        Assert.Equal(20, entity.LastEnqueuedSequenceNumber);
        Assert.False(entity.IsEmpty);
        Assert.Equal(11, entity.RetainedEventCount);
    }

    [Fact]
    public void HubPropertiesMapOntoTheTreesShapeWithTheGroupsKnownSeparately()
    {
        var properties = EventHubsModelFactory.EventHubProperties(
            "telemetry",
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ["0", "1", "2"]);

        var entity = EventMapper.ToEntity(properties, ["$Default", "analytics"]);

        Assert.Equal("telemetry", entity.Name);
        Assert.Equal(3, entity.PartitionCount);
        Assert.Equal(["$Default", "analytics"], entity.ConsumerGroups);
        Assert.False(entity.IsUnreachable);
    }


    [Fact]
    public void RetainedCountSpansTheSequenceRangeInclusively()
    {
        Assert.Equal(11, EventHubFixtures.Partition(beginning: 10, last: 20).RetainedEventCount);
    }

    [Fact]
    public void AnEmptyPartitionRetainsNothingEvenWhenItsCursorsHaveMoved()
    {
        // A partition drained by retention reports stale bounds alongside IsEmpty, so the
        // bounds alone would report a phantom event.
        Assert.Equal(0, EventHubFixtures.Partition(beginning: 21, last: 20, isEmpty: true).RetainedEventCount);
    }

    [Fact]
    public void AHubThatCouldNotBeDescribedKeepsItsNameAndItsReason()
    {
        var hub = EventHubEntity.Unreachable("typo-hub", "The messaging entity could not be found.");

        Assert.True(hub.IsUnreachable);
        Assert.Equal("typo-hub", hub.Name);
        Assert.Equal(0, hub.PartitionCount);
        Assert.Contains("could not be found", hub.Error);
    }
}

public class EventHubTreeNodeTests
{
    [Fact]
    public void APartitionNodeShowsItsRetainedCountAsABadge()
    {
        var node = new PartitionNodeViewModel(EventHubFixtures.Partition(beginning: 1, last: 1500));

        Assert.Equal("Partition 0", node.Title);
        Assert.Equal("1,500", node.ActiveBadge);
    }

    [Fact]
    public void AnEmptyPartitionShowsNoBadgeAtAll()
    {
        var node = new PartitionNodeViewModel(EventHubFixtures.Partition(isEmpty: true));

        Assert.Null(node.ActiveBadge);
        Assert.Equal("empty", node.Detail);
    }

    [Fact]
    public void AHubNodeCountsItsPartitions()
    {
        Assert.Equal("2 partitions", new EventHubNodeViewModel(EventHubFixtures.Hub()).Detail);
        Assert.Equal("1 partition", new EventHubNodeViewModel(EventHubFixtures.Hub(partitions: ["0"])).Detail);
    }

    [Fact]
    public void AnUnreachableHubIsDimmedRatherThanHidden()
    {
        var node = new EventHubNodeViewModel(EventHubEntity.Unreachable("typo-hub", "not found"));

        Assert.True(node.IsDisabled);
        Assert.Equal("typo-hub", node.Title);
    }

    [Fact]
    public void NodesFindTheirNamespaceThroughTheTree()
    {
        var (namespaceNode, hubNode, partitionNode) = EventHubFixtures.Tree();

        Assert.Same(namespaceNode, hubNode.Namespace);
        Assert.Same(namespaceNode, partitionNode.Namespace);
        Assert.True(namespaceNode.IsEventHubs);
    }

    [Fact]
    public void AnEventHubsNamespaceSaysSoInTheTree()
    {
        var node = new NamespaceNodeViewModel(EventHubFixtures.Connection());

        Assert.Equal("Event Hubs", node.Detail);
    }

    [Fact]
    public void AServiceBusNamespaceWithASasKeyCarriesNoDetailAtAll()
    {
        var node = new NamespaceNodeViewModel(new NamespaceConnection { Name = "contoso" });

        Assert.Null(node.Detail);
    }
}

public class EventHubSelectionTests
{
    [Fact]
    public void SelectingAHubOffersReadsButNotBrokerActions()
    {
        var (_, hub, _) = EventHubFixtures.Tree();
        var viewModel = new MainWindowViewModel { SelectedNode = hub };

        Assert.True(viewModel.HasMessageEntity);
        Assert.True(viewModel.IsEventHubSelection);

        // Receive, purge, delete and dead-letter have no Event Hubs equivalent.
        Assert.False(viewModel.CanUseBrokerActions);
        Assert.False(viewModel.CanEditEntity);
        Assert.False(viewModel.CanDeleteEntity);
        Assert.False(viewModel.CanManageEntities);
    }

    [Fact]
    public void SelectingAPartitionAlsoCountsAsAReadableEntity()
    {
        var (_, _, partition) = EventHubFixtures.Tree();
        var viewModel = new MainWindowViewModel { SelectedNode = partition };

        Assert.True(viewModel.HasMessageEntity);
        Assert.True(viewModel.IsEventHubSelection);
        Assert.Equal("telemetry / partition 0", viewModel.EntityHeader);
    }

    /// <summary>
    /// Leaving the toggle on from a previous Service Bus selection would send the next
    /// Peek at a sub-queue that does not exist.
    /// </summary>
    [Fact]
    public void MovingToAnEventHubClearsTheDeadLetterToggle()
    {
        var (_, hub, _) = EventHubFixtures.Tree();
        var viewModel = new MainWindowViewModel { ShowDeadLetter = true };

        viewModel.SelectedNode = hub;

        Assert.False(viewModel.ShowDeadLetter);
        Assert.Equal("Events", viewModel.MessageSourceLabel);
    }

    [Fact]
    public void SendingIsDisabledUntilTheNamespaceIsConnected()
    {
        var (_, hub, _) = EventHubFixtures.Tree();

        // The tree node exists, but no session has been opened, so there is no client.
        Assert.False(new MainWindowViewModel { SelectedNode = hub }.CanSend);
    }

    [Fact]
    public void TheSummaryNamesTheConsumerGroupReadsGoThrough()
    {
        var (_, hub, _) = EventHubFixtures.Tree(EventHubFixtures.Hub(consumerGroups: ["$Default", "analytics"]));
        var viewModel = new MainWindowViewModel { SelectedNode = hub };

        Assert.Contains("2 partitions", viewModel.EntitySummary);
        Assert.Contains("consumer groups: $Default, analytics", viewModel.EntitySummary);
        Assert.Contains("reading through $Default", viewModel.EntitySummary);
    }

    [Fact]
    public void AnUnreachableHubShowsItsErrorInsteadOfCounts()
    {
        var (_, hub, _) = EventHubFixtures.Tree(EventHubEntity.Unreachable("typo-hub", "not found"));
        var viewModel = new MainWindowViewModel { SelectedNode = hub };

        Assert.Equal("not found", viewModel.EntitySummary);
    }

    [Fact]
    public void TheEmptyStateExplainsThatReadingEventsIsSafe()
    {
        var (_, hub, _) = EventHubFixtures.Tree();
        var viewModel = new MainWindowViewModel { SelectedNode = hub };

        Assert.Contains("never remove", viewModel.EmptyMessagesHint);
    }

    [Fact]
    public void WithNothingSelectedTheHintMentionsEventHubsToo()
    {
        Assert.Contains("event hub", new MainWindowViewModel().EmptyMessagesHint);
    }
}

public class EventRowViewModelTests
{
    static MessageRowViewModel BuildEventRow() => new(new MessageRecord
    {
        Body = BinaryData.FromString("""{"deviceId":"probe-7"}"""),
        SourceEntityPath = "telemetry",
        SequenceNumber = 4021,
        EnqueuedTime = DateTimeOffset.UtcNow,
        Event = new EventOrigin("3", "112233")
    });

    [Fact]
    public void TheGridLeavesTheDeliveryColumnBlankForEvents()
    {
        Assert.Null(BuildEventRow().DeliveryCountText);
        Assert.Equal("3", BuildEventRow().PartitionId);
    }

    [Fact]
    public void ServiceBusRowsStillShowTheirDeliveryCount()
    {
        var row = new MessageRowViewModel(new MessageRecord
        {
            Body = BinaryData.FromString("x"),
            SourceEntityPath = "orders",
            DeliveryCount = 4
        });

        Assert.Equal("4", row.DeliveryCountText);
        Assert.Null(row.PartitionId);
    }

    [Fact]
    public void TheDetailPaneShowsWhereInTheLogAnEventCameFrom()
    {
        var names = BuildEventRow().PropertyEntries.Select(entry => entry.Name).ToList();

        Assert.Contains("Position", names);
        Assert.Contains("Partition", names);
        Assert.Contains("Offset", names);
    }

    /// <summary>
    /// Locks, expiry and redelivery are broker behaviour Event Hubs has none of. Showing
    /// them at their defaults would read as fact rather than as absence.
    /// </summary>
    [Fact]
    public void BrokerOnlyPropertiesAreLeftOffAnEvent()
    {
        var row = BuildEventRow();
        var names = row.PropertyEntries.Select(entry => entry.Name).ToList();

        Assert.DoesNotContain("Delivery count", names);
        Assert.DoesNotContain("Time to live", names);
        Assert.DoesNotContain("State", names);

        Assert.DoesNotContain("DeliveryCount", row.PropertiesText);
        Assert.DoesNotContain("TimeToLive", row.PropertiesText);
        Assert.Contains("PartitionId", row.PropertiesText);
        Assert.Contains("112233", row.PropertiesText);
    }
}

public class EventHubConnectionDialogTests
{
    [Fact]
    public void EventHubNamesAreSplitOnEveryFormatPeoplePaste()
    {
        Assert.Equal(
            ["telemetry", "audit", "billing"],
            ConnectionDialogViewModel.ParseEventHubNames("telemetry\naudit; billing"));
    }

    [Fact]
    public void DuplicateHubNamesAreDroppedRegardlessOfCase()
    {
        Assert.Equal(["telemetry"], ConnectionDialogViewModel.ParseEventHubNames("telemetry, TELEMETRY"));
    }

    [Fact]
    public void BlankInputIsNoHubsRatherThanOneEmptyName()
    {
        Assert.Empty(ConnectionDialogViewModel.ParseEventHubNames("  \n , ; "));
        Assert.Empty(ConnectionDialogViewModel.ParseEventHubNames(null));
    }

    [Fact]
    public void AnEventHubsNamespaceRoundTripsThroughTheDialog()
    {
        var original = EventHubFixtures.Connection();
        original.EventHubNames = ["telemetry", "audit"];
        original.ConsumerGroup = "analytics";
        original.SubscriptionId = "00000000-0000-0000-0000-000000000001";

        var saved = new ConnectionDialogViewModel(original).ToConnection();

        Assert.Equal(NamespaceKind.EventHubs, saved.Kind);
        Assert.Equal(["telemetry", "audit"], saved.EventHubNames);
        Assert.Equal("analytics", saved.ConsumerGroup);
        Assert.Equal("00000000-0000-0000-0000-000000000001", saved.SubscriptionId);
    }

    /// <summary>
    /// Switching a namespace back to Service Bus must not leave hub names behind, or the
    /// saved connection would describe a namespace it no longer points at.
    /// </summary>
    [Fact]
    public void SwitchingBackToServiceBusClearsTheEventHubSettings()
    {
        var original = EventHubFixtures.Connection();
        original.EventHubNames = ["telemetry"];
        original.ConsumerGroup = "analytics";

        var viewModel = new ConnectionDialogViewModel(original) { UseEventHubs = false };
        var saved = viewModel.ToConnection();

        Assert.Equal(NamespaceKind.ServiceBus, saved.Kind);
        Assert.Empty(saved.EventHubNames);
        Assert.Null(saved.ConsumerGroup);
    }

    [Fact]
    public void TheSubscriptionFieldOnlyAppliesToEntraDiscovery()
    {
        var viewModel = new ConnectionDialogViewModel(EventHubFixtures.Connection());

        // ARM has no way to accept a SAS key, so the field would be dead weight.
        Assert.True(viewModel.ShowEventHubFields);
        Assert.False(viewModel.ShowSubscriptionField);

        viewModel.UseEntraId = true;
        Assert.True(viewModel.ShowSubscriptionField);
    }

    [Fact]
    public void TheRoleHintNamesTheRolesTheChosenServiceActuallyUses()
    {
        var viewModel = new ConnectionDialogViewModel(null);
        Assert.Contains("Service Bus Data Owner", viewModel.RoleHint);

        viewModel.UseEventHubs = true;
        Assert.Contains("Event Hubs Data Receiver", viewModel.RoleHint);
    }
}

public class EventHubComposeTests
{
    [Fact]
    public void ComposingForAnEventHubHidesTheBrokerOnlyFields()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("telemetry", IsEventHub: true));

        Assert.False(viewModel.ShowServiceBusFields);
    }

    /// <summary>
    /// Event Hubs has no scheduled delivery. The checkbox is hidden, but a value left over
    /// from a previous compose must not produce a schedule the send path would ignore.
    /// </summary>
    [Fact]
    public void SchedulingIsIgnoredWhenComposingAnEvent()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("telemetry", IsEventHub: true))
        {
            Body = "hello",
            Schedule = true,
            ScheduledDate = DateTimeOffset.Now.AddDays(-1),
            ScheduledTime = TimeSpan.FromHours(9)
        };

        var result = viewModel.Build();

        // A past time would have failed validation on the Service Bus path.
        Assert.NotNull(result);
        Assert.Null(result.ScheduledEnqueueTime);
        Assert.False(viewModel.ShowScheduleFields);
    }

    [Fact]
    public void ComposingForAQueueKeepsTheBrokerFields()
    {
        var viewModel = new SendMessageViewModel(new SendMessageRequest("orders"));

        Assert.True(viewModel.ShowServiceBusFields);
        Assert.False(viewModel.IsEventHub);
    }
}

public class EventHubsConnectionStoreTests
{
    [Fact]
    public async Task AnEventHubsNamespaceSurvivesASaveAndReload()
    {
        var directory = Directory.CreateTempSubdirectory("sbmac-test");
        try
        {
            var store = new ConnectionStore(
                Path.Combine(directory.FullName, "connections.json"),
                new FileSecretStore(Path.Combine(directory.FullName, "secrets.json")));

            var original = EventHubFixtures.Connection();
            original.EventHubNames = ["telemetry", "audit"];
            original.ConsumerGroup = "analytics";
            original.SubscriptionId = "00000000-0000-0000-0000-000000000001";

            await store.SaveAsync([original], TestContext.Current.CancellationToken);
            var restored = Assert.Single(await store.LoadAsync(TestContext.Current.CancellationToken));

            Assert.Equal(NamespaceKind.EventHubs, restored.Kind);
            Assert.Equal(["telemetry", "audit"], restored.EventHubNames);
            Assert.Equal("analytics", restored.ConsumerGroup);
            Assert.Equal("00000000-0000-0000-0000-000000000001", restored.SubscriptionId);
            Assert.Equal(original.ConnectionString, restored.ConnectionString);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
