using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.UI.ViewModels;
using ClipStudio.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.Views.Settings;

/// <summary>
/// Code-behind for the Attention Required settings section.
/// Owns the one interaction that needs a window: the duplicate merge dialog.
/// </summary>
public partial class AttentionSectionView : UserControl
{
    /// <summary>Initialises a new <see cref="AttentionSectionView"/>.</summary>
    public AttentionSectionView()
    {
        InitializeComponent();
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    /// <summary>
    /// Hands the view model the one action that needs a window, then rebuilds the list.
    /// </summary>
    /// <remarks>
    /// The rebuild is not redundant. An entry captures its action when it is built, and the list is
    /// built at startup - before any view exists - so the badge is right from the first frame. A
    /// duplicate entry created then has no merge action, because this hook had not run yet, and it
    /// renders without its button. That stayed invisible while duplicates only ever appeared after
    /// a scan started from this very section; restoring known groups at startup exposed it.
    /// </remarks>
    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is not AttentionSectionViewModel vm)
            return;

        vm.MergeRequested = MergeAsync;
        _ = vm.RebuildAsync();
    }

    /// <summary>
    /// Shows the merge dialog for one duplicate group.
    /// </summary>
    /// <param name="group">The group to resolve.</param>
    /// <returns>
    /// Whether the merge was applied. False when there is no window to show a dialog over, so a
    /// headless context changes nothing.
    /// </returns>
    private async Task<bool> MergeAsync(DuplicateClipGroup group)
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return false;
        if (App.Services is null) return false;

        // LibVLC is resolved rather than required: if it cannot be created on this machine the
        // dialog still opens and can still merge, just without the preview.
        LibVLCSharp.Shared.LibVLC? libVlc = null;
        try { libVlc = App.Services.GetService<LibVLCSharp.Shared.LibVLC>(); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "No LibVLC for the duplicate preview."); }

        var dialogVm = new DuplicateMergeViewModel(
            group,
            App.Services.GetRequiredService<IServiceScopeFactory>(),
            App.Services.GetRequiredService<IDuplicateClipFinder>(),
            libVlc);

        var dialog = new DuplicateMergeDialog { DataContext = dialogVm };
        dialogVm.CloseRequested = applied => dialog.Close(applied);

        await dialogVm.LoadAsync();

        // Closing the window with its title bar returns null; nothing was applied.
        return await dialog.ShowDialog<bool?>(window) ?? false;
    }
}
