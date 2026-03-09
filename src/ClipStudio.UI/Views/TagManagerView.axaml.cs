using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="TagManagerView"/>.
/// Triggers an initial data load when the view is attached to the visual tree.
/// Handles row-tap events to open the edit panel.
/// </summary>
public partial class TagManagerView : UserControl
{
    /// <summary>Initialises a new <see cref="TagManagerView"/> and wires up component events.</summary>
    public TagManagerView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is TagManagerViewModel vm)
            vm.LoadCommand.Execute(null);
    }

    /// <summary>
    /// Opens the edit form for the tapped tag row, unless the tap originated on a Button
    /// (e.g. the Delete button).
    /// </summary>
    private void OnTagRowTapped(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Visual src && src.FindAncestorOfType<Button>(includeSelf: true) is not null)
            return;

        if (sender is Border { DataContext: TagRowViewModel row })
            row.EditCommand.Execute(null);
    }
}
