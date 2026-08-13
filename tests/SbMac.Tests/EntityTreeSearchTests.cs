using Azure.Messaging.ServiceBus.Administration;

using System.Reflection;

using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Tree;

using SbMac.Core.Connections;
using SbMac.Core.Entities;

using Xunit;

namespace SbMac.Tests;

public class EntityTreeSearchTests
{
    [Fact]
    public void SearchMatchesQueueNamesWithoutCaringAboutCase()
    {
        var tree = BuildTree();

        tree.ViewModel.EntitySearchText = "VOIC";

        Assert.True(tree.Invoices.IsVisible);
        Assert.False(tree.Orders.IsVisible);
    }

    [Fact]
    public void SearchToleratesATransposedPairOfCharacters()
    {
        var tree = BuildTree();

        tree.ViewModel.EntitySearchText = "ordres";

        Assert.True(tree.Orders.IsVisible);
        Assert.False(tree.Invoices.IsVisible);
    }

    [Fact]
    public void MatchingATopicKeepsAllOfItsSubscriptionsVisible()
    {
        var tree = BuildTree();

        tree.ViewModel.EntitySearchText = "order-evnts";

        Assert.True(tree.OrderEvents.IsVisible);
        Assert.True(tree.Audit.IsVisible);
        Assert.True(tree.Billing.IsVisible);
    }

    [Fact]
    public void MatchingASubscriptionShowsItsTopicButHidesNonMatchingSiblings()
    {
        var tree = BuildTree();

        tree.ViewModel.EntitySearchText = "aduit";

        Assert.True(tree.Namespace.IsVisible);
        Assert.True(tree.Topics.IsVisible);
        Assert.True(tree.OrderEvents.IsVisible);
        Assert.True(tree.Audit.IsVisible);
        Assert.False(tree.Billing.IsVisible);
        Assert.False(tree.Queues.IsVisible);
    }

    [Fact]
    public void ClearingSearchRestoresTheWholeTree()
    {
        var tree = BuildTree();
        tree.ViewModel.EntitySearchText = "does-not-exist";

        tree.ViewModel.EntitySearchText = string.Empty;

        Assert.All(AllNodes(tree), node => Assert.True(node.IsVisible));
    }

    [Fact]
    public void ClearingSearchRestoresThePreviousExpansionState()
    {
        var tree = BuildTree();
        tree.Namespace.IsExpanded = false;
        tree.Topics.IsExpanded = false;
        tree.OrderEvents.IsExpanded = false;

        tree.ViewModel.EntitySearchText = "audit";
        Assert.True(tree.Namespace.IsExpanded);
        Assert.True(tree.Topics.IsExpanded);
        Assert.True(tree.OrderEvents.IsExpanded);

        tree.ViewModel.EntitySearchText = string.Empty;

        Assert.False(tree.Namespace.IsExpanded);
        Assert.False(tree.Topics.IsExpanded);
        Assert.False(tree.OrderEvents.IsExpanded);
    }

    [Fact]
    public void NodesLoadedWhileSearchIsActiveUseTheCurrentSearch()
    {
        var tree = BuildEmptyTree();
        tree.ViewModel.EntitySearchText = "orders";

        var invoices = Queue("invoices", tree.Queues);
        tree.Queues.Children.Add(invoices);
        var orders = Queue("orders", tree.Queues);
        tree.Queues.Children.Add(orders);

        Assert.False(invoices.IsVisible);
        Assert.True(orders.IsVisible);
        Assert.True(tree.Queues.IsVisible);
        Assert.True(tree.Namespace.IsVisible);
    }

    static SearchTree BuildTree()
    {
        var empty = BuildEmptyTree();
        var orders = Queue("orders", empty.Queues);
        var invoices = Queue("invoices", empty.Queues);
        empty.Queues.Children.Add(orders);
        empty.Queues.Children.Add(invoices);

        var orderEvents = new TopicNodeViewModel(new TopicEntity(Model<TopicProperties>("order-events"), null))
        {
            Parent = empty.Topics
        };
        empty.Topics.Children.Add(orderEvents);

        var audit = Subscription("order-events", "audit", orderEvents);
        var billing = Subscription("order-events", "billing", orderEvents);
        orderEvents.Children.Add(audit);
        orderEvents.Children.Add(billing);

        return new SearchTree(
            empty.ViewModel,
            empty.Namespace,
            empty.Queues,
            empty.Topics,
            orders,
            invoices,
            orderEvents,
            audit,
            billing);
    }

    static EmptySearchTree BuildEmptyTree()
    {
        var viewModel = new MainWindowViewModel();
        var namespaceNode = new NamespaceNodeViewModel(new NamespaceConnection { Name = "contoso" });
        var queues = new QueueFolderNodeViewModel { Parent = namespaceNode };
        var topics = new TopicFolderNodeViewModel { Parent = namespaceNode };
        namespaceNode.Children.Add(queues);
        namespaceNode.Children.Add(topics);
        viewModel.Namespaces.Add(namespaceNode);
        return new EmptySearchTree(viewModel, namespaceNode, queues, topics);
    }

    static QueueNodeViewModel Queue(string name, TreeNodeViewModel parent) =>
        new(new QueueEntity(Model<QueueProperties>(name), null)) { Parent = parent };

    static SubscriptionNodeViewModel Subscription(string topic, string name, TreeNodeViewModel parent) =>
        new(new SubscriptionEntity(Model<SubscriptionProperties>(topic, name), null)) { Parent = parent };

    // The Azure SDK keeps these model constructors internal because production instances
    // normally come from the service. Tests need only their names.
    static T Model<T>(params object[] arguments) => (T)Activator.CreateInstance(
        typeof(T), BindingFlags.Instance | BindingFlags.NonPublic, null, arguments, null)!;

    static TreeNodeViewModel[] AllNodes(SearchTree tree) =>
    [
        tree.Namespace,
        tree.Queues,
        tree.Topics,
        tree.Orders,
        tree.Invoices,
        tree.OrderEvents,
        tree.Audit,
        tree.Billing
    ];

    sealed record EmptySearchTree(
        MainWindowViewModel ViewModel,
        NamespaceNodeViewModel Namespace,
        QueueFolderNodeViewModel Queues,
        TopicFolderNodeViewModel Topics);

    sealed record SearchTree(
        MainWindowViewModel ViewModel,
        NamespaceNodeViewModel Namespace,
        QueueFolderNodeViewModel Queues,
        TopicFolderNodeViewModel Topics,
        QueueNodeViewModel Orders,
        QueueNodeViewModel Invoices,
        TopicNodeViewModel OrderEvents,
        SubscriptionNodeViewModel Audit,
        SubscriptionNodeViewModel Billing);
}
