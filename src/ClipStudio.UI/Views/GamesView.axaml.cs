using Avalonia.Controls;
using Avalonia.Input;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="GamesView"/>.
/// Triggers an initial data load when the view is attached to the visual tree
/// and handles keyboard shortcuts (Enter to search).
/// </summary>
public partial class GamesView : UserControl
{
    /// <summary>Initialises a new <see cref="GamesView"/> and wires up component events.</summary>
    public GamesView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is GamesViewModel vm)
            vm.LoadCommand.Execute(null);
    }

    /// <summary>Fires the search command when the user presses Enter in the search query box.</summary>
    private void OnSearchQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is GamesViewModel vm)
            vm.SearchCommand.Execute(null);
    }
}
