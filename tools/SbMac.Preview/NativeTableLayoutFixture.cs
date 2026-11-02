using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Azure.Messaging.ServiceBus.Administration;
using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Tree;
using SbMac.App.Views;
using SbMac.Core.Entities;
using SbMac.Core.Messaging;

namespace SbMac.Preview;

/// <summary>Synthetic rows and observable selection for native layout gestures; no broker access.</summary>
static class NativeTableLayoutFixture
{
    public static void Seed(MainWindow window, MainWindowViewModel vm)
    {
        var ns = vm.Namespaces.Single();
        var queue = new QueueNodeViewModel(new QueueEntity(
            Named<QueueProperties>("orders-fixture"), Named<QueueRuntimeProperties>("orders-fixture")))
        { Parent = ns, IsSelected = true };
        ns.Children.Add(queue);
        ns.IsExpanded = true;
        vm.SelectedNode = queue;
        for (var i = 1; i <= 3; i++)
            vm.Messages.Add(new MessageRowViewModel(new MessageRecord
            {
                SequenceNumber = i, MessageId = $"fixture-{i}", Subject = "layout check",
                EnqueuedTime = DateTimeOffset.UnixEpoch,
                Body = BinaryData.FromString($"Synthetic message {i}"), SourceEntityPath = "orders-fixture"
            }));
        window.UpdateLayout();
        var grid = window.FindControl<DataGrid>("MessageGrid")!;
        grid.SelectedItems.Add(vm.Messages[0]);
        grid.SelectedItems.Add(vm.Messages[1]);
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(vm.MessageColumns) or nameof(vm.MessageTableContext))
                Snapshot(vm);
        };
        window.Closing += (_, _) => Snapshot(vm);
        Snapshot(vm);
    }

    static T Named<T>(string name) => (T)Activator.CreateInstance(typeof(T),
        BindingFlags.Instance | BindingFlags.NonPublic, null, [name], null)!;

    static void Snapshot(MainWindowViewModel vm) => Console.WriteLine("TABLE_LAYOUT=" + JsonSerializer.Serialize(new
    {
        context = vm.MessageTableContext.ToString(),
        selected = vm.SelectedMessages.Select(r => r.MessageId).Order().ToArray(),
        selectedFlags = vm.Messages.Where(r => r.IsSelected).Select(r => r.MessageId).Order().ToArray(),
        focused = vm.SelectedMessage?.MessageId,
        columns = vm.MessageColumns
    }));
}
