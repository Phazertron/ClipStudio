using Avalonia.Controls;
using Avalonia.Interactivity;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="UnreviewedQueueView"/>.
/// Triggers an initial data load when the view is attached to the visual tree,
/// and routes row tap events to the <see cref="UnreviewedQueueViewModel"/>.
/// </summary>
public partial class UnreviewedQueueView : UserControl
{
    /// <summary>Initialises a new <see cref="UnreviewedQueueView"/> and wires up component events.</summary>
    public UnreviewedQueueView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is UnreviewedQueueViewModel vm)
            vm.LoadCommand.Execute(null);
    }

    /// <summary>
    /// Handles a tap on a clip row and requests the parent VM to open the clip's detail view.
    /// </summary>
    private void OnClipRowTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control control &&
            control.DataContext is ClipCardViewModel card &&
            DataContext is UnreviewedQueueViewModel vm)
        {
            vm.OpenClip(card.ClipId);
        }
    }
}
