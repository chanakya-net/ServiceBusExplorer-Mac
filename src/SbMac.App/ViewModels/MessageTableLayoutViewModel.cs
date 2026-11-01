namespace SbMac.App.ViewModels;

public enum MessageTableContext { ServiceBus, DeadLetter, EventHub }

public sealed record MessageColumnMetadata(string Id, bool IsVisible);

public sealed class MessageTableLayoutViewModel
{
    static readonly string[] Original = ["selection", "sequence", "enqueued", "messageId", "label", "size", "deliveryCount", "reason", "preview"];
    static readonly string[] DeadLetter = ["selection", "sequence", "reason", "deliveryCount", "enqueued", "messageId", "label", "size", "preview"];

    public static IReadOnlyList<MessageColumnMetadata> ForContext(MessageTableContext context) =>
        (context == MessageTableContext.DeadLetter ? DeadLetter : Original)
        .Select(id => new MessageColumnMetadata(id, context != MessageTableContext.ServiceBus || id is not ("deliveryCount" or "reason")))
        .ToArray();

    public static IReadOnlyList<string> ColumnIds => Original;
}
