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
            if (scenario is 2 or 4 or 6)
                Assert.True(dialog.GetVisualDescendants().OfType<NumericUpDown>().First().IsKeyboardFocusWithin);
            if (scenario == 8)
                Assert.True(dialog.GetVisualDescendants().OfType<ComboBox>().First().IsKeyboardFocusWithin);
            foreach (var field in dialog.GetVisualDescendants().OfType<Control>()
                .Where(c => c is TextBox or NumericUpDown or ComboBox))
                if (field.IsEffectivelyVisible && field.IsEffectivelyEnabled && field.TemplatedParent is null)
                    Assert.False(string.IsNullOrEmpty(AutomationProperties.GetName(field)), field.Name ?? field.GetType().Name);
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
