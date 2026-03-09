using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Read-only view model projection of a single <see cref="Highlight"/> row
/// displayed in the Highlights page.
/// Exposes a tapped delegate so the row can be opened by clicking anywhere on it.
/// Also supports asynchronous thumbnail loading identical to <see cref="ClipCardViewModel"/>.
/// </summary>
public sealed partial class HighlightRowViewModel : ViewModelBase
{
    /// <summary>Gets the unique identifier of the highlight.</summary>
    public int HighlightId { get; }

    /// <summary>Gets the unique identifier of the parent clip.</summary>
    public int ClipId { get; }

    /// <summary>Gets the label of this highlight, or a placeholder when none is set.</summary>
    public string Label { get; }

    /// <summary>Gets the file name of the parent clip.</summary>
    public string ClipName { get; }

    /// <summary>Gets the start time formatted as <c>h:mm:ss</c>.</summary>
    public string StartTimeDisplay { get; }

    /// <summary>Gets the end time formatted as <c>h:mm:ss</c>.</summary>
    public string EndTimeDisplay { get; }

    /// <summary>Gets the duration of the highlight formatted as <c>h:mm:ss</c>.</summary>
    public string DurationDisplay { get; }

    /// <summary>Gets a comma-separated list of tag names applied to this highlight.</summary>
    public string TagsDisplay { get; }

    /// <summary>Gets the optional notes text, or empty string if none.</summary>
    public string Notes { get; }

    /// <summary>Gets the creation timestamp formatted for display.</summary>
    public string CreatedAtDisplay { get; }

    /// <summary>Gets the raw creation timestamp used for sorting.</summary>
    public DateTime CreatedAt { get; }

    /// <summary>Gets the raw start time for sorting and watch-mode purposes.</summary>
    public TimeSpan StartTime { get; }

    /// <summary>Gets the raw end time for watch-mode purposes.</summary>
    public TimeSpan EndTime { get; }

    /// <summary>Gets the raw duration for sorting purposes.</summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Gets the display name of the game associated with the parent clip,
    /// derived from the clip's Game-type tag. Empty string if none assigned.
    /// </summary>
    public string GameDisplay { get; }

    /// <summary>
    /// Gets the tag identifier of the game tag on the parent clip, or null if none assigned.
    /// Used for game-based filtering in <see cref="HighlightsPageViewModel"/>.
    /// </summary>
    public int? GameTagId { get; }

    /// <summary>
    /// Gets a comma-separated list of player display names associated with the parent clip.
    /// Empty string if no players are tagged.
    /// </summary>
    public string PlayerDisplay { get; }

    /// <summary>
    /// Gets the absolute path to the highlight thumbnail image, or null if not yet generated.
    /// </summary>
    public string? ThumbnailPath { get; }

    /// <summary>
    /// Gets or sets the decoded thumbnail bitmap loaded asynchronously.
    /// Null until <see cref="LoadThumbnailAsync"/> completes.
    /// </summary>
    [ObservableProperty]
    private Bitmap? _thumbnailBitmap;

    // ---- Cover art and player icons ----

    private readonly string? _gameCoverUrl;
    private readonly long? _gameStoreAppId;
    private readonly IReadOnlyList<string?> _playerIconPaths;

    private static readonly string CoverCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipStudio", "covers");

    private static readonly HttpClient _http = new();

    /// <summary>
    /// Gets or sets the game cover art bitmap loaded asynchronously from the Steam CDN cache.
    /// Null until <see cref="LoadImagesAsync"/> completes or when the game has no cover URL.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameCoverBitmap))]
    private Bitmap? _gameCoverBitmap;

    /// <summary>Gets whether a game cover bitmap has been loaded.</summary>
    public bool HasGameCoverBitmap => GameCoverBitmap != null;

    /// <summary>
    /// Gets or sets the list of player icon bitmaps loaded asynchronously.
    /// Contains only bitmaps for players that have a valid icon file.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayerIconBitmaps))]
    private IReadOnlyList<Bitmap?> _playerIconBitmaps = Array.Empty<Bitmap?>();

    /// <summary>Gets whether at least one player icon bitmap has been loaded.</summary>
    public bool HasPlayerIconBitmaps => PlayerIconBitmaps.Count > 0;

    /// <summary>
    /// Delegate invoked when the user clicks on this highlight row to open it in watch mode.
    /// </summary>
    private readonly Action<HighlightRowViewModel>? _onWatch;

    /// <summary>
    /// Initialises a new <see cref="HighlightRowViewModel"/> from a <see cref="Highlight"/> entity.
    /// </summary>
    /// <param name="highlight">The highlight entity to project.</param>
    /// <param name="onWatch">
    /// Action invoked when the row is tapped by the user.
    /// Receives this row so the caller can locate its position in the sequence for prev/next navigation.
    /// </param>
    public HighlightRowViewModel(Highlight highlight, Action<HighlightRowViewModel>? onWatch = null)
    {
        _onWatch = onWatch;

        HighlightId    = highlight.Id;
        ClipId         = highlight.ClipId;
        Label          = string.IsNullOrWhiteSpace(highlight.Label) ? "(unlabelled)" : highlight.Label;
        ClipName       = Path.GetFileNameWithoutExtension(
                             highlight.Clip?.FileName ?? highlight.Clip?.FilePath ?? $"Clip {highlight.ClipId}");
        StartTime          = highlight.StartTime;
        EndTime            = highlight.EndTime;
        Duration           = highlight.Duration;
        CreatedAt          = highlight.CreatedAt;
        StartTimeDisplay   = FormatTime(highlight.StartTime);
        EndTimeDisplay     = FormatTime(highlight.EndTime);
        DurationDisplay    = FormatTime(highlight.Duration);
        TagsDisplay        = highlight.HighlightTags.Count > 0
            ? string.Join(", ", highlight.HighlightTags
                                         .Select(ht => ht.Tag?.Name ?? string.Empty)
                                         .Where(n => n.Length > 0))
            : string.Empty;
        Notes            = highlight.Notes ?? string.Empty;
        CreatedAtDisplay = highlight.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        ThumbnailPath    = highlight.ThumbnailPath;

        // Derive game and player display from parent clip navigation properties.
        var gameTag = highlight.Clip?.ClipTags
            .Select(ct => ct.Tag)
            .FirstOrDefault(t => t?.Type == TagType.Game);
        GameDisplay    = gameTag?.Name ?? string.Empty;
        GameTagId      = gameTag?.Id;
        _gameCoverUrl  = gameTag?.GameCoverUrl;
        _gameStoreAppId = gameTag?.GameStoreAppId;

        PlayerDisplay = highlight.Clip?.ClipPlayers.Count > 0
            ? string.Join(", ", highlight.Clip.ClipPlayers
                                              .Select(cp => cp.Player?.DisplayName ?? string.Empty)
                                              .Where(n => n.Length > 0))
            : string.Empty;
        _playerIconPaths = highlight.Clip?.ClipPlayers
            .Select(cp => cp.Player?.IconPath)
            .ToList() ?? new List<string?>();
    }

    /// <summary>
    /// Called by the view's Tapped handler to open this highlight in watch mode.
    /// </summary>
    public void RequestWatch() => _onWatch?.Invoke(this);

    /// <summary>
    /// Asynchronously decodes the highlight thumbnail image on a background thread and stores it in
    /// <see cref="ThumbnailBitmap"/> so the UI thread is not blocked.
    /// </summary>
    public async Task LoadThumbnailAsync()
    {
        if (string.IsNullOrEmpty(ThumbnailPath) || !File.Exists(ThumbnailPath))
            return;

        try
        {
            var bitmap = await Task.Run(() => new Bitmap(ThumbnailPath));
            ThumbnailBitmap = bitmap;
        }
        catch
        {
            // Thumbnail decode failure is non-fatal; the row shows no image instead.
        }
    }

    /// <summary>
    /// Asynchronously loads the game cover art from the local disk cache (or Steam CDN) and
    /// loads any player icon bitmaps from their local file paths. Failures are silently ignored.
    /// </summary>
    public async Task LoadImagesAsync()
    {
        // Game cover art.
        if (!string.IsNullOrEmpty(_gameCoverUrl))
        {
            try
            {
                var cachePath = _gameStoreAppId.HasValue
                    ? Path.Combine(CoverCacheDir, $"{_gameStoreAppId}.jpg")
                    : null;

                byte[]? bytes = null;
                if (cachePath is not null && File.Exists(cachePath))
                {
                    bytes = await File.ReadAllBytesAsync(cachePath);
                }
                else
                {
                    bytes = await _http.GetByteArrayAsync(_gameCoverUrl);
                    if (cachePath is not null)
                    {
                        Directory.CreateDirectory(CoverCacheDir);
                        await File.WriteAllBytesAsync(cachePath, bytes);
                    }
                }

                using var ms = new MemoryStream(bytes);
                GameCoverBitmap = new Bitmap(ms);
            }
            catch
            {
                // Cover art is optional; silently ignore network or decode failures.
            }
        }

        // Player icon bitmaps.
        var icons = new List<Bitmap?>();
        foreach (var iconPath in _playerIconPaths)
        {
            if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                continue;
            try
            {
                var bmp = await Task.Run(() => new Bitmap(iconPath));
                icons.Add(bmp);
            }
            catch
            {
                // Icon decode failure is non-fatal.
            }
        }
        PlayerIconBitmaps = icons;
    }

    private static string FormatTime(TimeSpan ts)
        => ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
}
