using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ClipStudio.Application.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model projection for a single Steam game result displayed in the game search dropdown.
/// Loads the cover art bitmap asynchronously from the Steam CDN.
/// Exposes a <see cref="SelectCommand"/> delegate that the parent VM wires up on construction.
/// </summary>
public sealed partial class GameSearchResultViewModel : ViewModelBase
{
    /// <summary>Gets the underlying <see cref="SteamGame"/> data transfer object.</summary>
    public SteamGame Game { get; }

    /// <summary>Gets the display label showing the game title.</summary>
    public string DisplayLabel => Game.Name;

    /// <summary>Gets or sets a value indicating whether this result is currently selected.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Gets or sets the cover art bitmap loaded asynchronously from the Steam CDN.</summary>
    [ObservableProperty]
    private Bitmap? _coverBitmap;

    /// <summary>Gets the command that selects or deselects this result.</summary>
    public IRelayCommand SelectCommand { get; }

    /// <summary>Initialises a new <see cref="GameSearchResultViewModel"/>.</summary>
    /// <param name="game">The Steam game entry to project.</param>
    /// <param name="onSelect">Callback invoked when the user clicks this result.</param>
    public GameSearchResultViewModel(SteamGame game, Action<GameSearchResultViewModel> onSelect)
    {
        Game          = game;
        SelectCommand = new RelayCommand(() => onSelect(this));
    }

    /// <summary>
    /// Loads the cover art bitmap asynchronously from the game's cover URL.
    /// Silently ignores network or decoding errors.
    /// </summary>
    /// <param name="http">The HTTP client used to download the image.</param>
    public async Task LoadCoverAsync(HttpClient http)
    {
        if (string.IsNullOrEmpty(Game.CoverUrl))
            return;

        try
        {
            var bytes = await http.GetByteArrayAsync(Game.CoverUrl);
            using var ms = new MemoryStream(bytes);
            CoverBitmap = new Bitmap(ms);
        }
        catch (Exception)
        {
            // Cover art is optional; silently ignore failures.
        }
    }
}
