using Avalonia.Controls;
using Avalonia.Controls.Templates;

using SbMac.App.ViewModels;

namespace SbMac.App;

/// <summary>
/// Maps a view model to its view by name: <c>…ViewModels.FooViewModel</c> renders as
/// <c>…Views.FooView</c>. Keeps the XAML free of explicit DataTemplate wiring.
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        var name = param.GetType().FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(name);

        return type is not null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = $"No view found for {name}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
