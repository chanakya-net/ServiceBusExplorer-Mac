using Azure;
using Azure.Messaging.ServiceBus.Administration;

namespace SbMac.Core.Entities;

/// <summary>
/// Browse and edit the entities in a namespace: queues, topics, subscriptions and rules.
/// </summary>
public sealed class EntityService
{
    readonly ServiceBusSession session;

    public EntityService(ServiceBusSession session)
    {
        this.session = session;
    }

    ServiceBusAdministrationClient Admin => session.Administration;

    // ---------------------------------------------------------------- queues

    /// <summary>
    /// Lists every queue with its counters. Definitions and runtime counters are two
    /// separate paged calls, so we fetch both and join by name rather than issuing one
    /// runtime call per queue.
    /// </summary>
    public async Task<IReadOnlyList<QueueEntity>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        var definitions = new List<QueueProperties>();
        await foreach (var queue in Admin.GetQueuesAsync(cancellationToken).ConfigureAwait(false))
        {
            definitions.Add(queue);
        }

        var runtime = new Dictionary<string, QueueRuntimeProperties>(StringComparer.OrdinalIgnoreCase);
        await foreach (var properties in Admin.GetQueuesRuntimePropertiesAsync(cancellationToken).ConfigureAwait(false))
        {
            runtime[properties.Name] = properties;
        }

        return definitions
            .OrderBy(queue => queue.Name, StringComparer.OrdinalIgnoreCase)
            .Select(queue => new QueueEntity(queue, runtime.GetValueOrDefault(queue.Name)))
            .ToList();
    }

    public async Task<QueueEntity> GetQueueAsync(string name, CancellationToken cancellationToken = default)
    {
        var properties = await Admin.GetQueueAsync(name, cancellationToken).ConfigureAwait(false);
        var runtime = await Admin.GetQueueRuntimePropertiesAsync(name, cancellationToken).ConfigureAwait(false);
        return new QueueEntity(properties.Value, runtime.Value);
    }

    public async Task<QueueEntity> CreateQueueAsync(CreateQueueOptions options, CancellationToken cancellationToken = default)
    {
        var created = await Admin.CreateQueueAsync(options, cancellationToken).ConfigureAwait(false);
        return new QueueEntity(created.Value, null);
    }

    /// <summary>
    /// Applies changes to an existing queue. Service Bus rejects updates to
    /// create-only settings (partitioning, sessions, duplicate detection), so the UI
    /// must keep those read-only once a queue exists.
    /// </summary>
    public async Task<QueueEntity> UpdateQueueAsync(QueueProperties properties, CancellationToken cancellationToken = default)
    {
        var updated = await Admin.UpdateQueueAsync(properties, cancellationToken).ConfigureAwait(false);
        return new QueueEntity(updated.Value, null);
    }

    public async Task DeleteQueueAsync(string name, CancellationToken cancellationToken = default)
    {
        await Admin.DeleteQueueAsync(name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Enables or disables a queue by flipping its status.</summary>
    public async Task SetQueueStatusAsync(string name, EntityStatus status, CancellationToken cancellationToken = default)
    {
        var properties = (await Admin.GetQueueAsync(name, cancellationToken).ConfigureAwait(false)).Value;
        properties.Status = status;
        await Admin.UpdateQueueAsync(properties, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> QueueExistsAsync(string name, CancellationToken cancellationToken = default) =>
        (await Admin.QueueExistsAsync(name, cancellationToken).ConfigureAwait(false)).Value;

    // ---------------------------------------------------------------- topics

    public async Task<IReadOnlyList<TopicEntity>> GetTopicsAsync(CancellationToken cancellationToken = default)
    {
        var definitions = new List<TopicProperties>();
        await foreach (var topic in Admin.GetTopicsAsync(cancellationToken).ConfigureAwait(false))
        {
            definitions.Add(topic);
        }

        var runtime = new Dictionary<string, TopicRuntimeProperties>(StringComparer.OrdinalIgnoreCase);
        await foreach (var properties in Admin.GetTopicsRuntimePropertiesAsync(cancellationToken).ConfigureAwait(false))
        {
            runtime[properties.Name] = properties;
        }

        return definitions
            .OrderBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
            .Select(topic => new TopicEntity(topic, runtime.GetValueOrDefault(topic.Name)))
            .ToList();
    }

    public async Task<TopicEntity> GetTopicAsync(string name, CancellationToken cancellationToken = default)
    {
        var properties = await Admin.GetTopicAsync(name, cancellationToken).ConfigureAwait(false);
        var runtime = await Admin.GetTopicRuntimePropertiesAsync(name, cancellationToken).ConfigureAwait(false);
        return new TopicEntity(properties.Value, runtime.Value);
    }

    public async Task<TopicEntity> CreateTopicAsync(CreateTopicOptions options, CancellationToken cancellationToken = default)
    {
        var created = await Admin.CreateTopicAsync(options, cancellationToken).ConfigureAwait(false);
        return new TopicEntity(created.Value, null);
    }

    public async Task<TopicEntity> UpdateTopicAsync(TopicProperties properties, CancellationToken cancellationToken = default)
    {
        var updated = await Admin.UpdateTopicAsync(properties, cancellationToken).ConfigureAwait(false);
        return new TopicEntity(updated.Value, null);
    }

    public async Task DeleteTopicAsync(string name, CancellationToken cancellationToken = default)
    {
        await Admin.DeleteTopicAsync(name, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetTopicStatusAsync(string name, EntityStatus status, CancellationToken cancellationToken = default)
    {
        var properties = (await Admin.GetTopicAsync(name, cancellationToken).ConfigureAwait(false)).Value;
        properties.Status = status;
        await Admin.UpdateTopicAsync(properties, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TopicExistsAsync(string name, CancellationToken cancellationToken = default) =>
        (await Admin.TopicExistsAsync(name, cancellationToken).ConfigureAwait(false)).Value;

    // --------------------------------------------------------- subscriptions

    public async Task<IReadOnlyList<SubscriptionEntity>> GetSubscriptionsAsync(
        string topicName,
        CancellationToken cancellationToken = default)
    {
        var definitions = new List<SubscriptionProperties>();
        await foreach (var subscription in Admin.GetSubscriptionsAsync(topicName, cancellationToken).ConfigureAwait(false))
        {
            definitions.Add(subscription);
        }

        var runtime = new Dictionary<string, SubscriptionRuntimeProperties>(StringComparer.OrdinalIgnoreCase);
        await foreach (var properties in Admin
            .GetSubscriptionsRuntimePropertiesAsync(topicName, cancellationToken)
            .ConfigureAwait(false))
        {
            runtime[properties.SubscriptionName] = properties;
        }

        return definitions
            .OrderBy(subscription => subscription.SubscriptionName, StringComparer.OrdinalIgnoreCase)
            .Select(subscription => new SubscriptionEntity(
                subscription,
                runtime.GetValueOrDefault(subscription.SubscriptionName)))
            .ToList();
    }

    public async Task<SubscriptionEntity> GetSubscriptionAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken = default)
    {
        var properties = await Admin
            .GetSubscriptionAsync(topicName, subscriptionName, cancellationToken).ConfigureAwait(false);
        var runtime = await Admin
            .GetSubscriptionRuntimePropertiesAsync(topicName, subscriptionName, cancellationToken).ConfigureAwait(false);
        return new SubscriptionEntity(properties.Value, runtime.Value);
    }

    public async Task<SubscriptionEntity> CreateSubscriptionAsync(
        CreateSubscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        var created = await Admin.CreateSubscriptionAsync(options, cancellationToken).ConfigureAwait(false);
        return new SubscriptionEntity(created.Value, null);
    }

    /// <summary>
    /// Creates a subscription whose default rule is <paramref name="rule"/> rather than
    /// the <c>1=1</c> catch-all, which is the only chance to set it — the default rule
    /// can't be replaced atomically afterwards.
    /// </summary>
    public async Task<SubscriptionEntity> CreateSubscriptionAsync(
        CreateSubscriptionOptions options,
        CreateRuleOptions rule,
        CancellationToken cancellationToken = default)
    {
        var created = await Admin.CreateSubscriptionAsync(options, rule, cancellationToken).ConfigureAwait(false);
        return new SubscriptionEntity(created.Value, null);
    }

    public async Task<SubscriptionEntity> UpdateSubscriptionAsync(
        SubscriptionProperties properties,
        CancellationToken cancellationToken = default)
    {
        var updated = await Admin.UpdateSubscriptionAsync(properties, cancellationToken).ConfigureAwait(false);
        return new SubscriptionEntity(updated.Value, null);
    }

    public async Task DeleteSubscriptionAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken = default)
    {
        await Admin.DeleteSubscriptionAsync(topicName, subscriptionName, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetSubscriptionStatusAsync(
        string topicName,
        string subscriptionName,
        EntityStatus status,
        CancellationToken cancellationToken = default)
    {
        var properties = (await Admin
            .GetSubscriptionAsync(topicName, subscriptionName, cancellationToken).ConfigureAwait(false)).Value;
        properties.Status = status;
        await Admin.UpdateSubscriptionAsync(properties, cancellationToken).ConfigureAwait(false);
    }

    // ----------------------------------------------------------------- rules

    public async Task<IReadOnlyList<RuleProperties>> GetRulesAsync(
        string topicName,
        string subscriptionName,
        CancellationToken cancellationToken = default)
    {
        var rules = new List<RuleProperties>();
        await foreach (var rule in Admin
            .GetRulesAsync(topicName, subscriptionName, cancellationToken)
            .ConfigureAwait(false))
        {
            rules.Add(rule);
        }

        return rules.OrderBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<RuleProperties> CreateRuleAsync(
        string topicName,
        string subscriptionName,
        CreateRuleOptions options,
        CancellationToken cancellationToken = default)
    {
        var created = await Admin
            .CreateRuleAsync(topicName, subscriptionName, options, cancellationToken).ConfigureAwait(false);
        return created.Value;
    }

    public async Task<RuleProperties> UpdateRuleAsync(
        string topicName,
        string subscriptionName,
        RuleProperties rule,
        CancellationToken cancellationToken = default)
    {
        var updated = await Admin
            .UpdateRuleAsync(topicName, subscriptionName, rule, cancellationToken).ConfigureAwait(false);
        return updated.Value;
    }

    public async Task DeleteRuleAsync(
        string topicName,
        string subscriptionName,
        string ruleName,
        CancellationToken cancellationToken = default)
    {
        await Admin.DeleteRuleAsync(topicName, subscriptionName, ruleName, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ namespace

    /// <summary>
    /// Reads namespace-level quota information. Returns null when the signed-in
    /// identity can't read it, which is common for entity-scoped SAS keys.
    /// </summary>
    public async Task<NamespaceProperties?> TryGetNamespacePropertiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var properties = await Admin.GetNamespacePropertiesAsync(cancellationToken).ConfigureAwait(false);
            return properties.Value;
        }
        catch (RequestFailedException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
