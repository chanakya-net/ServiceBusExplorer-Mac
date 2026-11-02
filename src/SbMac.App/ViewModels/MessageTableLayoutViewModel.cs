using SbMac.App.Models;

namespace SbMac.App.ViewModels;

public enum MessageTableContext { ServiceBus, DeadLetter, EventHub }

public sealed record MessageColumnMetadata(string Id, bool IsVisible, double? Width = null);

public sealed class MessageTableLayoutViewModel
{
    static readonly string[] Original = ["selection", "sequence", "enqueued", "messageId", "label", "size", "deliveryCount", "reason", "preview", "partition", "offset"];
    static readonly string[] EventHub = ["selection", "partition", "sequence", "offset", "enqueued", "size", "preview", "messageId", "label", "deliveryCount", "reason"];
    static readonly string[] DeadLetter = ["selection", "sequence", "reason", "deliveryCount", "enqueued", "messageId", "label", "size", "preview", "partition", "offset"];

    public static IReadOnlyList<MessageColumnMetadata> ForContext(MessageTableContext context) =>
        (context switch { MessageTableContext.DeadLetter => DeadLetter, MessageTableContext.EventHub => EventHub, _ => Original })
        .Select(id => new MessageColumnMetadata(id, IsVisible(context, id)))
        .ToArray();

    // Event Hubs has no delivery, dead-letter or message-id/label facts, so those stay hidden
    // rather than showing broker defaults; partition/offset exist only for events.
    static bool IsVisible(MessageTableContext context, string id) => context switch
    {
        MessageTableContext.EventHub => id is not ("messageId" or "label" or "deliveryCount" or "reason"),
        MessageTableContext.DeadLetter => id is not ("partition" or "offset"),
        _ => id is not ("deliveryCount" or "reason" or "partition" or "offset")
    };

    public static IReadOnlyList<MessageColumnMetadata> Resolve(MessageTableContext context, IEnumerable<MessageColumnState>? saved)
    {
        var defaults = ForContext(context);
        var values = new Dictionary<string, MessageColumnState>();
        foreach (var state in saved ?? [])
            if (state.Id is not null) values.TryAdd(state.Id, state.Normalize());
        var result = defaults.Select(c => values.TryGetValue(c.Id, out var state) ? c with { Width = state.Width } : c)
            .Select(c => c.Id == "selection" ? c with { IsVisible = true }
            : !IsApplicable(context, c.Id) ? c with { IsVisible = false }
            : values.TryGetValue(c.Id, out var state) ? c with { IsVisible = state.Visible } : c).ToArray();
        if (!result.Any(c => c.Id != "selection" && c.IsVisible))
        {
            var first = Array.FindIndex(result, c => c.Id != "selection" && defaults.Any(d => d.Id == c.Id && d.IsVisible));
            result[first] = result[first] with { IsVisible = true };
        }
        return result.OrderBy(c => c.Id == "selection" ? -1
            : values.TryGetValue(c.Id, out var savedState) && savedState.DisplayIndex is > 0 && savedState.DisplayIndex < defaults.Count
                ? savedState.DisplayIndex.Value : int.MaxValue).ToArray();
    }

    public static bool IsApplicable(MessageTableContext context, string id) => id != "selection" && context switch
    {
        MessageTableContext.EventHub => id is not ("messageId" or "label" or "deliveryCount" or "reason"),
        _ => id is not ("partition" or "offset")
    };

    public static IReadOnlyList<string> ColumnIds => Original;
}
