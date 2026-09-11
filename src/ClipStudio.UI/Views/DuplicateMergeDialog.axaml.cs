using System;
using Avalonia.Controls;

namespace ClipStudio.UI.Views;

/// <summary>
/// Dialog that resolves one group of duplicate clips.
/// </summary>
/// <remarks>
/// Owns nothing but the window: the view model closes it through its
/// <see cref="ViewModels.DuplicateMergeViewModel.CloseRequested"/> callback, reporting whether the
/// merge was applied so the caller can refresh.
/// </remarks>
public partial class DuplicateMergeDialog : Window
{
    /// <summary>Initialises a new <see cref="DuplicateMergeDialog"/>.</summary>
    public DuplicateMergeDialog()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The player is attached here rather than bound in XAML. LibVLC embeds into the control's
    /// native handle at the moment the player is assigned, and before the window is open there is
    /// no handle to embed into - it then falls back to opening its own floating
    /// "VLC (Direct3D11 output)" window, which is what happened while this was a binding.
    /// </remarks>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is ViewModels.DuplicateMergeViewModel vm)
            PreviewVideo.MediaPlayer = vm.MediaPlayer;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Releases the preview player before the window goes away. It holds a native handle and keeps
    /// the clip's file open, which on Windows would block the deletion the merge is about to do.
    /// </remarks>
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as ViewModels.DuplicateMergeViewModel)?.DisposePreview();
        base.OnClosed(e);
    }
}
