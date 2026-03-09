using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type is null)
            return new TextBlock { Text = "Not Found: " + name };

        try
        {
            return (Control)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            return new TextBlock
            {
                Text     = $"View error ({type.Name}): {ex.GetType().Name}: {ex.Message}",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            };
        }
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
