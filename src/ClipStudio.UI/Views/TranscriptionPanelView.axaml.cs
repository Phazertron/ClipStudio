using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for <see cref="TranscriptionPanelView"/>.
/// Wires the pointer-press on each segment row to the seek callback via the
/// <see cref="ViewModels.TranscriptionSegmentViewModel.RequestSeek"/> method.
/// </summary>
public partial class TranscriptionPanelView : UserControl
{
    /// <summary>Initialises a new <see cref="TranscriptionPanelView"/>.</summary>
    public TranscriptionPanelView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnSegmentRowPressed, handledEventsToo: false);
    }

    private void OnSegmentRowPressed(object? sender, PointerPressedEventArgs e)
    {
        // Walk up from the pressed element to find the Border with a
        // TranscriptionSegmentViewModel DataContext and invoke RequestSeek.
        var element = e.Source as Avalonia.Visual;
        while (element is not null)
        {
            if (element is Border { DataContext: ViewModels.TranscriptionSegmentViewModel segVm })
            {
                segVm.RequestSeek();
                e.Handled = true;
                return;
            }
            element = element.GetVisualParent();
        }
    }
}
