using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SbMac.App.Views;
using SbMac.Core.ImportExport;
using Xunit;

namespace SbMac.Tests;

public partial class UiSmokeTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public async Task DialogKeyboardRoundTripsPreserveDefaultsAndOwnerFocus(int scenario)
    {
        var owner = new MainWindow { DataContext = NewIsolatedViewModel() };
        owner.Show();
        try
        {
            var invoker = owner.GetControl<TextBox>("EntitySearchBox");
            invoker.Focus(NavigationMethod.Tab);
            var pending = OpenKeyboardDialog(owner, scenario);
            var dialog = Assert.Single(owner.OwnedWindows);
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.IsKeyboardFocusWithin);
            if (scenario is 0 or 1 or 3 or 5 or 7)
                Assert.True(dialog.GetControl<TextBox>("NameField").IsFocused);
            if (scenario is 2 or 4 or 6 or 8)
                Assert.True(dialog.GetControl<Control>(scenario switch
                    { 6 => "MaxDeliveryCountField", 8 => "FilterTypeField", _ => "MaxSizeField" }).IsKeyboardFocusWithin);
            foreach (var field in dialog.GetVisualDescendants().OfType<Control>()
                .Where(c => c is TextBox or NumericUpDown or ComboBox))
                if (field.IsEffectivelyVisible && field.IsEffectivelyEnabled && field.TemplatedParent is null)
                    AssertNamedAfterVisibleLabel(field);
            Press(dialog, Key.Tab);
            Assert.True(dialog.IsKeyboardFocusWithin);
            Press(dialog, Key.Tab, Avalonia.Input.RawInputModifiers.Shift);
            Press(dialog, Key.Escape);
            await pending;
            Assert.Empty(owner.OwnedWindows);
            Assert.True(invoker.IsKeyboardFocusWithin);
        }
        finally { owner.Close(); }
    }

    static void AssertNamedAfterVisibleLabel(Control field)
    {
        var name = AutomationProperties.GetName(field);
        Assert.False(string.IsNullOrEmpty(name), field.Name ?? field.GetType().Name);
        // Speech and voice-control users address a field by the label they can see.
        if (field.Parent is Panel row && row.Children.OfType<TextBlock>()
                .FirstOrDefault(t => t.Classes.Contains("fieldLabel")) is { Text: { } label })
            Assert.Equal(label, name);
    }

    static Task OpenKeyboardDialog(MainWindow owner, int scenario) => scenario switch
    {
        0 => owner.EditConnectionAsync(null),
        1 or 2 => owner.EditQueueAsync(new QueueDefinition { Name = "orders" }, scenario == 1),
        3 or 4 => owner.EditTopicAsync(new TopicDefinition { Name = "orders" }, scenario == 3),
        5 or 6 => owner.EditSubscriptionAsync("events", new SubscriptionDefinition { Name = "orders" }, scenario == 5),
        7 or 8 => owner.EditRuleAsync(new RuleDefinition { Name = "orders", SqlExpression = "1=1" }, scenario == 7),
        9 => owner.ConfirmAsync("Delete fixture", "Fixture only", "Delete", true),
        10 => owner.ShowInfoAsync("Fixture", "Information"),
        11 => owner.ChooseResubmitActionAsync(1, "orders"),
        _ => owner.ChooseImportPolicyAsync(new NamespaceDefinition())
    };
}

public partial class UiSmokeTests
{
    [AvaloniaTheory]
    [InlineData(Key.Enter, true)]
    [InlineData(Key.Escape, false)]
    public async Task DestructiveConfirmationIgnoresSpaceAtOpen(Key finalKey, bool expected)
    {
        var owner = new Window();
        owner.Show();
        try
        {
            var result = SbMac.App.Views.Dialogs.MessageDialog.ConfirmAsync(owner, "Purge", "Purge all?", "Purge", true);
            var dialog = Assert.Single(owner.OwnedWindows);
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.IsKeyboardFocusWithin);
            Press(dialog, Key.Space);
            Assert.False(result.IsCompleted);
            Assert.True(dialog.IsVisible);
            Press(dialog, finalKey);
            Assert.Equal(expected, await result);
        }
        finally { owner.Close(); }
    }
}
