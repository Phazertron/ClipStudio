using Avalonia.Controls;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="ExportQueueView"/>.
/// Triggers an initial data load when the view is attached to the visual tree.
/// </summary>
public partial class ExportQueueView : UserControl
{
    /// <summary>Initialises a new <see cref="ExportQueueView"/> and wires up component events.</summary>
    public ExportQueueView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is ExportQueueViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
