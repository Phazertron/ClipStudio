using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model projection for a single Game-type <see cref="Tag"/> row in the Games page list.
/// Loads the game's cover art bitmap asynchronously, checking a local disk cache before the Steam CDN.
/// </summary>
public sealed partial class GameRowViewModel : ViewModelBase
{
    private static readonly string CacheDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipStudio", "covers");

    /// <summary>Gets the unique identifier of the underlying tag.</summary>
    public int TagId { get; }

    /// <summary>Gets the display name of the game.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether this game tag is linked to a Steam app entry.</summary>
    public bool HasGameLink { get; }

    /// <summary>Gets the number of clips that carry this game tag.</summary>
    public int ClipCount { get; }

    /// <summary>Gets the cover art URL from the Steam CDN, or null for custom game tags.</summary>
    public string? GameCoverUrl { get; }

    /// <summary>Gets the Steam app ID used as the disk cache key, or null for custom game tags.</summary>
    public long? GameStoreAppId { get; }

    /// <summary>Gets or sets the cover art bitmap loaded asynchronously.</summary>
    [ObservableProperty]
    private Bitmap? _coverBitmap;

    /// <summary>Gets the command that requests the parent VM to open an edit form for this game.</summary>
    public IRelayCommand EditCommand { get; }

    /// <summary>Gets the command that deletes this game tag after delegating to the parent VM.</summary>
    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>Gets the command that navigates to the Library page pre-filtered by this game tag.</summary>
    public IRelayCommand ViewInLibraryCommand { get; }

    /// <summary>Gets the auto-detect alias chips associated with this game tag.</summary>
    public ObservableCollection<GameAliasChipViewModel> Aliases { get; } = new();

    /// <summary>Gets a value indicating whether any auto-detect aliases are linked to this game tag.</summary>
    public bool HasAliases => Aliases.Count > 0;

    /// <summary>
    /// Initialises a new <see cref="GameRowViewModel"/> from an entity and parent callbacks.
    /// </summary>
    /// <param name="tag">The Game-type tag entity to project.</param>
    /// <param name="onEdit">Callback invoked when the user clicks Edit.</param>
    /// <param name="onDelete">Callback invoked when the user confirms deletion.</param>
    /// <param name="onViewInLibrary">Callback invoked when the user clicks "View in Library". Receives the tag ID.</param>
    public GameRowViewModel(Tag tag, Action<GameRowViewModel> onEdit, Func<GameRowViewModel, Task> onDelete,
                            Action<int>? onViewInLibrary = null)
    {
        TagId          = tag.Id;
        Name           = tag.Name;
        HasGameLink    = tag.GameStoreAppId.HasValue;
        ClipCount      = tag.ClipTags.Count;
        GameCoverUrl   = tag.GameCoverUrl;
        GameStoreAppId = tag.GameStoreAppId;

        EditCommand          = new RelayCommand(() => onEdit(this));
        DeleteCommand        = new AsyncRelayCommand(() => onDelete(this));
        ViewInLibraryCommand = new RelayCommand(() => onViewInLibrary?.Invoke(TagId),
                                                canExecute: () => onViewInLibrary is not null);
    }

    /// <summary>
    /// Returns the local cache path for this game's cover art, or null when no app ID is set.
    /// </summary>
    private string? GetCachePath() =>
        GameStoreAppId.HasValue
            ? System.IO.Path.Combine(CacheDir, $"{GameStoreAppId}.jpg")
            : null;

    /// <summary>
    /// Loads the cover art bitmap asynchronously, preferring the local disk cache over the
    /// Steam CDN to minimise network round-trips. Downloaded images are saved to the cache.
    /// Silently ignores network or decoding errors.
    /// </summary>
    /// <param name="http">The HTTP client used to download the image when not cached.</param>
    public async Task LoadCoverAsync(HttpClient http)
    {
        if (string.IsNullOrEmpty(GameCoverUrl))
            return;

        try
        {
            var cachePath = GetCachePath();

            byte[]? bytes = null;

            // Prefer local cache when available.
            if (cachePath is not null && File.Exists(cachePath))
            {
                bytes = await File.ReadAllBytesAsync(cachePath);
            }
            else
            {
                bytes = await http.GetByteArrayAsync(GameCoverUrl);

                // Persist to cache for future loads.
                if (cachePath is not null)
                {
                    Directory.CreateDirectory(CacheDir);
                    await File.WriteAllBytesAsync(cachePath, bytes);
                }
            }

            using var ms = new MemoryStream(bytes);
            CoverBitmap = new Bitmap(ms);
        }
        catch (Exception)
        {
            // Cover art is optional; silently ignore failures.
        }
    }
}
