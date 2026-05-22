using System;
using Avalonia;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Lightweight view model that pairs a player icon bitmap with the display size to use
/// in the library details-row icon strip. Size is driven by <see cref="ClipCardViewModel.DetailsIconSize"/>.
/// </summary>
public sealed class PlayerIconSlotViewModel
{
    /// <summary>Gets the decoded player icon bitmap, or <c>null</c> when no icon is set (renders placeholder).</summary>
    public Bitmap? Icon { get; }

    /// <summary>Gets the icon diameter in pixels.</summary>
    public int Size { get; }

    /// <summary>Gets the corner radius for the circular clip border (half of <see cref="Size"/>).</summary>
    public CornerRadius Radius => new CornerRadius(Size / 2.0);

    /// <summary>Gets the database identifier of the player this slot represents.</summary>
    public int PlayerId { get; }

    /// <summary>
    /// Gets the command that fires the quick-filter callback with <see cref="PlayerId"/>.
    /// Null when no quick-filter callback is set.
    /// </summary>
    public IRelayCommand? QuickFilterCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="PlayerIconSlotViewModel"/>.
    /// </summary>
    /// <param name="icon">The player icon bitmap, or <c>null</c> for placeholder rendering.</param>
    /// <param name="size">The icon diameter in pixels.</param>
    /// <param name="playerId">The database identifier of the player.</param>
    /// <param name="quickFilter">Optional callback invoked when the user clicks the icon to quick-filter.</param>
    public PlayerIconSlotViewModel(Bitmap? icon, int size, int playerId, Action<int>? quickFilter = null)
    {
        Icon = icon;
        Size = size;
        PlayerId = playerId;
        if (quickFilter is not null)
            QuickFilterCommand = new RelayCommand(() => quickFilter(playerId));
    }
}
