using System.Reflection;

using SbMac.App.Services;
using SbMac.App.ViewModels.Tree;
using SbMac.Core;
using SbMac.Core.Messaging;

namespace SbMac.Tests;

/// <summary>
/// Minimal <see cref="IUiServices"/> that records every call and approves every prompt,
/// so a command that wrongly reaches a confirmation shows up as a recorded call.
/// </summary>
public class RecordingUi : DispatchProxy
{
    public List<string> Calls { get; } = [];

    /// <summary>Answer for ConfirmAsync; the control case declines so nothing reaches Azure.</summary>
    public bool Approve { get; set; } = true;

    public static (IUiServices Ui, RecordingUi Recorder) Create()
    {
        var ui = Create<IUiServices, RecordingUi>();
        return (ui, (RecordingUi)(object)ui);
    }

    /// <summary>Gives the node an offline <see cref="MessageService"/> so guards, not nulls, decide.</summary>
    public static void AttachOfflineMessages(NamespaceNodeViewModel node)
    {
        var session = ServiceBusSession.Create(node.Connection);
        typeof(NamespaceNodeViewModel).GetProperty(nameof(NamespaceNodeViewModel.Messages))!
            .SetValue(node, new MessageService(session));
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Calls.Add(targetMethod!.Name);
        var type = targetMethod.ReturnType;
        if (type == typeof(Task<bool>))
            return Task.FromResult(Approve);
        if (type == typeof(Task))
            return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(type.GetGenericArguments()[0])
                .Invoke(null, [type.GetGenericArguments()[0].IsValueType
                    ? Activator.CreateInstance(type.GetGenericArguments()[0]) : null]);
        return null;
    }
}
