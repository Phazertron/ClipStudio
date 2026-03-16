using Avalonia.Media.Imaging;

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
    public int Radius => Size / 2;

    /// <summary>
    /// Initialises a new <see cref="PlayerIconSlotViewModel"/>.
    /// </summary>
    /// <param name="icon">The player icon bitmap, or <c>null</c> for placeholder rendering.</param>
    /// <param name="size">The icon diameter in pixels.</param>
    public PlayerIconSlotViewModel(Bitmap? icon, int size)
    {
        Icon = icon;
        Size = size;
    }
}
