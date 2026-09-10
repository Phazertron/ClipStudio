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

    private void OnAttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (DataContext is AttentionSectionViewModel vm)
            vm.MergeRequested = MergeAsync;
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

        var dialogVm = new DuplicateMergeViewModel(
            group,
            App.Services.GetRequiredService<IServiceScopeFactory>(),
            App.Services.GetRequiredService<IDuplicateClipFinder>());

        var dialog = new DuplicateMergeDialog { DataContext = dialogVm };
        dialogVm.CloseRequested = applied => dialog.Close(applied);

        await dialogVm.LoadAsync();

        // Closing the window with its title bar returns null; nothing was applied.
        return await dialog.ShowDialog<bool?>(window) ?? false;
    }
}
