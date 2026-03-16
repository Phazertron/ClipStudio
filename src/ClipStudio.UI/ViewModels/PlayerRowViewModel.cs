using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model projection for a single <see cref="Player"/> row in the Players page list.
/// Loads the player's icon bitmap asynchronously from disk.
/// </summary>
public sealed partial class PlayerRowViewModel : ViewModelBase
{
    /// <summary>Gets the unique identifier of the underlying player.</summary>
    public int PlayerId { get; }

    /// <summary>Gets the primary display name of the player.</summary>
    public string Name { get; }

    /// <summary>Gets a comma-separated list of aliases for this player.</summary>
    public string AliasesDisplay { get; }

    /// <summary>Gets a value indicating whether this player represents the local user.</summary>
    public bool IsMe { get; }

    /// <summary>Gets the absolute path to the icon image, or null when no icon is set.</summary>
    public string? IconPath { get; }

    /// <summary>Gets or sets the icon bitmap loaded asynchronously from disk.</summary>
    [ObservableProperty]
    private Bitmap? _iconBitmap;

    /// <summary>Gets the command that requests the parent VM to open an edit form for this player.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Gets the command that deletes this player after delegating to the parent VM.</summary>
    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>Gets the command that navigates to the Library page pre-filtered by this player.</summary>
    public IRelayCommand ViewInLibraryCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="PlayerRowViewModel"/> from an entity and parent callbacks.
    /// </summary>
    /// <param name="player">The player entity to project.</param>
    /// <param name="onEdit">Callback invoked when the user clicks Edit.</param>
    /// <param name="onDelete">Async callback invoked when the user confirms deletion.</param>
    /// <param name="onViewInLibrary">Callback invoked when the user clicks "View in Library". Receives the player ID.</param>
    public PlayerRowViewModel(Player player, Action<PlayerRowViewModel> onEdit, Func<PlayerRowViewModel, Task> onDelete,
                              Action<int>? onViewInLibrary = null)
    {
        PlayerId      = player.Id;
        Name          = player.DisplayName;
        IsMe          = player.IsMe;
        IconPath      = player.IconPath;
        AliasesDisplay = player.Aliases.Count > 0
            ? string.Join(", ", player.Aliases.Select(a => a.Alias))
            : string.Empty;

        EditCommand          = new RelayCommand(() => onEdit(this));
        DeleteCommand        = new AsyncRelayCommand(() => onDelete(this));
        ViewInLibraryCommand = new RelayCommand(() => onViewInLibrary?.Invoke(PlayerId),
                                                canExecute: () => onViewInLibrary is not null);
    }

    /// <summary>
    /// Loads the icon bitmap asynchronously from the configured file path.
    /// Silently ignores errors if the file is missing or unreadable.
    /// </summary>
    public async Task LoadIconAsync()
    {
        if (string.IsNullOrEmpty(IconPath) || !File.Exists(IconPath))
            return;

        try
        {
            IconBitmap = await Task.Run(() => new Bitmap(IconPath));
        }
        catch (Exception)
        {
            // Icon is optional; silently ignore load failures.
        }
    }
}
