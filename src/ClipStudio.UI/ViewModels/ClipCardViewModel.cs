using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model that represents a single clip displayed as a card in the library grid
/// or unreviewed queue. Exposes flattened, display-ready properties from a <see cref="Clip"/>
/// and supports async thumbnail loading and hover-scrub preview-strip animation.
/// </summary>
public sealed partial class ClipCardViewModel : ViewModelBase
{
    /// <summary>Gets the unique database identifier of the clip.</summary>
    public int ClipId { get; }

    /// <summary>Gets the absolute path to the video file on disk.</summary>
    public string FilePath { get; }

    /// <summary>Gets or sets the file name of the clip (without directory path).</summary>
    [ObservableProperty] private string _fileName = string.Empty;

    /// <summary>Gets the absolute path to the generated thumbnail image, or null if not yet generated.</summary>
    public string? ThumbnailPath { get; }

    /// <summary>Gets the absolute path to the preview sprite-sheet image, or null if not generated.</summary>
    public string? StripPath { get; }

    /// <summary>
    /// Gets or sets the decoded thumbnail bitmap loaded asynchronously.
    /// Null until <see cref="LoadThumbnailAsync"/> completes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStaticThumbnail))]
    private Bitmap? _thumbnailBitmap;

    /// <summary>
    /// Gets or sets the decoded preview strip bitmap loaded lazily on first hover.
    /// Null until <see cref="LoadStripAsync"/> completes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStaticThumbnail))]
    [NotifyPropertyChangedFor(nameof(StripFrameOffset))]
    private Bitmap? _stripBitmap;

    /// <summary>
    /// Gets or sets whether the pointer is currently hovering over the card thumbnail.
    /// Toggled by pointer enter / exit events in the view code-behind.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStaticThumbnail))]
    [NotifyPropertyChangedFor(nameof(IsCheckboxVisible))]
    private bool _isHovering;

    /// <summary>
    /// Gets or sets whether this card is currently selected (checked) in multi-select mode.
    /// When changed, calls <see cref="SelectionChanged"/> so the parent VM can maintain its selection set.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCheckboxVisible))]
    private bool _isSelected;

    /// <summary>
    /// Gets or sets whether this card is the source clip in copy-format mode.
    /// When true the card renders a "source" visual indicator and cannot be selected as a target.
    /// </summary>
    [ObservableProperty] private bool _isCopyFormatSource;

    /// <summary>
    /// Gets or sets whether this card was the most recently opened clip.
    /// Used to show a subtle highlight after the user returns from the detail view.
    /// </summary>
    [ObservableProperty] private bool _isLastVisited;

    /// <summary>
    /// Gets or sets the row height in pixels for the details-view row.
    /// Set by <see cref="LibraryViewModel"/> from persisted settings.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailsThumbnailHeight))]
    [NotifyPropertyChangedFor(nameof(DetailsFontSize))]
    [NotifyPropertyChangedFor(nameof(DetailsIconSize))]
    [NotifyPropertyChangedFor(nameof(DetailsIconRadius))]
    [NotifyPropertyChangedFor(nameof(DetailsStarSize))]
    [NotifyPropertyChangedFor(nameof(PlayerIconSlots))]
    private int _detailsRowHeight = 52;

    /// <summary>
    /// Gets the thumbnail panel height derived from <see cref="DetailsRowHeight"/>.
    /// Ensures the thumbnail fills most of the row while leaving a small margin.
    /// </summary>
    public int DetailsThumbnailHeight => Math.Max(24, DetailsRowHeight - 4);

    /// <summary>
    /// Gets the font size for detail-row text labels, scaled with <see cref="DetailsRowHeight"/>.
    /// </summary>
    public int DetailsFontSize => DetailsRowHeight switch
    {
        <= 38 => 10,
        <= 50 => 11,
        <= 62 => 12,
        <= 80 => 13,
        _     => 14,
    };

    /// <summary>
    /// Gets the player icon diameter in pixels, scaled with <see cref="DetailsRowHeight"/>.
    /// </summary>
    public int DetailsIconSize => DetailsRowHeight switch
    {
        <= 38 => 18,
        <= 52 => 22,
        <= 72 => 28,
        <= 100 => 34,
        _      => 40,
    };

    /// <summary>
    /// Gets the corner radius for circular player icon borders (half of <see cref="DetailsIconSize"/>).
    /// </summary>
    public int DetailsIconRadius => DetailsIconSize / 2;

    /// <summary>
    /// Gets the rating star and favourite heart icon size in pixels, scaled with <see cref="DetailsRowHeight"/>.
    /// </summary>
    public int DetailsStarSize => DetailsRowHeight switch
    {
        <= 38 => 10,
        <= 52 => 12,
        _     => 14,
    };

    /// <summary>
    /// Gets whether the selection checkbox should be visible.
    /// Visible while hovering (so users can discover multi-select) and while the card is selected
    /// (so selected state remains visible after the pointer leaves).
    /// </summary>
    public bool IsCheckboxVisible => IsHovering || IsSelected;

    /// <summary>
    /// Gets or sets the horizontal scrub fraction (0–1) set by pointer-move events.
    /// Drives which frame of the preview strip is displayed.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StripFrameOffset))]
    private double _scrubFraction;

    /// <summary>Gets the number of frames in the preview strip sprite-sheet (default 20).</summary>
    public int StripFrameCount { get; }

    /// <summary>Gets the total pixel width of the preview strip image (FrameCount × card width 220 px).</summary>
    public double StripTotalPixelWidth => StripFrameCount * 220.0;

    /// <summary>
    /// Gets the horizontal margin offset applied to the strip image so that the correct
    /// frame is visible within the 220 px card. Updated every time <see cref="ScrubFraction"/> changes.
    /// </summary>
    public Thickness StripFrameOffset =>
        new Thickness(-Math.Floor(ScrubFraction * StripFrameCount) * 220.0, 0, 0, 0);

    /// <summary>
    /// Gets whether to show the static thumbnail. True when not hovering or when the strip
    /// bitmap has not yet been loaded.
    /// </summary>
    public bool ShowStaticThumbnail => !IsHovering || StripBitmap is null;

    /// <summary>Gets the duration of the clip.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets the date and time the clip was originally recorded (parsed from filename or file system).</summary>
    public DateTime RecordedAt { get; }

    /// <summary>Gets the current review status of the clip.</summary>
    public ClipStatus Status { get; }

    /// <summary>Gets a value indicating whether the clip is in the <see cref="ClipStatus.Archived"/> state.</summary>
    public bool IsArchived => Status == ClipStatus.Archived;

    /// <summary>
    /// Gets or sets whether the "archived clip" notice strip is visible on this card.
    /// Set to <see langword="true"/> when the user taps an archived card to prompt them
    /// to re-enable the source folder in Settings.
    /// </summary>
    [ObservableProperty]
    private bool _isArchivedNoticeVisible;

    /// <summary>Gets or sets whether the source file for this clip is missing or unreadable on disk.</summary>
    [ObservableProperty] private bool _isBroken;

    /// <summary>Gets the star rating of the clip (0 = unrated, 1-5 = rated).</summary>
    public int Rating { get; }

    /// <summary>Gets a value indicating whether the clip is marked as a favourite.</summary>
    public bool IsFavourite { get; }

    /// <summary>Gets the tag identifier of the game tag assigned to this clip, or null if no game tag is set.</summary>
    public int? GameTagId { get; }

    /// <summary>Gets the tag identifiers of all general tags applied to this clip.</summary>
    public IReadOnlyList<int> GeneralTagIds { get; }

    /// <summary>Gets the player identifiers of all players tagged on this clip.</summary>
    public IReadOnlyList<int> PlayerTagIds { get; }

    // ---- Star rating bool helpers ----

    /// <summary>Gets whether the clip has a rating of at least 1 star.</summary>
    public bool IsRated1OrMore => Rating >= 1;

    /// <summary>Gets whether the clip has a rating of at least 2 stars.</summary>
    public bool IsRated2OrMore => Rating >= 2;

    /// <summary>Gets whether the clip has a rating of at least 3 stars.</summary>
    public bool IsRated3OrMore => Rating >= 3;

    /// <summary>Gets whether the clip has a rating of at least 4 stars.</summary>
    public bool IsRated4OrMore => Rating >= 4;

    /// <summary>Gets whether the clip has a rating of at least 5 stars.</summary>
    public bool IsRated5OrMore => Rating >= 5;

    // ---- Async-loaded cover art and player icons ----

    private readonly string? _gameCoverUrl;
    private readonly long? _gameStoreAppId;
    private readonly IReadOnlyList<string?> _playerIconPaths;

    private static readonly string CoverCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClipStudio", "covers");

    private static readonly HttpClient _http = new();

    /// <summary>
    /// Gets or sets the game cover art bitmap loaded asynchronously from the Steam CDN cache.
    /// Null until <see cref="LoadImagesAsync"/> completes or if the game has no cover URL.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGameCoverBitmap))]
    private Bitmap? _gameCoverBitmap;

    /// <summary>Gets whether a game cover bitmap has been loaded.</summary>
    public bool HasGameCoverBitmap => GameCoverBitmap != null;

    /// <summary>
    /// Gets or sets the list of player icon bitmaps loaded asynchronously.
    /// Contains one entry per player: the loaded <see cref="Bitmap"/> when an icon file is
    /// available, or <c>null</c> to render a default placeholder icon.
    /// Empty until <see cref="LoadImagesAsync"/> completes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlayerIconBitmaps))]
    [NotifyPropertyChangedFor(nameof(PlayerIconSlots))]
    private IReadOnlyList<Bitmap?> _playerIconBitmaps = Array.Empty<Bitmap?>();

    /// <summary>
    /// Gets whether at least one player has a custom icon bitmap.
    /// When true the icon strip is shown; when false plain text is used.
    /// </summary>
    public bool HasPlayerIconBitmaps => PlayerIconBitmaps.Any(b => b is not null);

    /// <summary>
    /// Gets the player icon slots for the details-row icon strip, each pairing a bitmap
    /// with the current <see cref="DetailsIconSize"/> so the template can use typed bindings.
    /// Re-evaluated whenever <see cref="PlayerIconBitmaps"/> or <see cref="DetailsIconSize"/> changes.
    /// </summary>
    public IReadOnlyList<PlayerIconSlotViewModel> PlayerIconSlots =>
        PlayerIconBitmaps
            .Select((b, i) => new PlayerIconSlotViewModel(
                b, DetailsIconSize,
                i < PlayerTagIds.Count ? PlayerTagIds[i] : 0,
                QuickFilterPlayerRequested))
            .ToList();

    // ---- Quick-filter callbacks (set by LibraryViewModel after card creation) ----

    /// <summary>
    /// Optional callback invoked when the user clicks the game element in the details row to
    /// add that game as a library filter chip. Set by <see cref="LibraryViewModel"/> after creation.
    /// </summary>
    public Action<int>? QuickFilterGameRequested { get; set; }

    /// <summary>
    /// Optional callback invoked when the user clicks a player icon in the details row to
    /// add that player as a library filter chip. Set by <see cref="LibraryViewModel"/> after creation.
    /// </summary>
    public Action<int>? QuickFilterPlayerRequested { get; set; }

    /// <summary>
    /// Gets the command that adds this clip's game to the library filter chip list.
    /// Enabled only when a game tag is set.
    /// </summary>
    public IRelayCommand QuickFilterByGameCommand { get; }

    /// <summary>
    /// Gets the game name suggested by the OBS filename parser, or null if none was detected.
    /// Used in the Unreviewed queue to prompt the user to confirm or change the game tag.
    /// </summary>
    public string? SuggestedGameName { get; }

    /// <summary>Gets the confirmed game name for this clip, or null if no game tag is set.</summary>
    public string? GameName { get; }

    /// <summary>Gets the general tags applied to this clip, joined by commas, or an empty string if none.</summary>
    public string TagsDisplay { get; }

    /// <summary>Gets the player names tagged on this clip, joined by commas, or an empty string if none.</summary>
    public string PlayersDisplay { get; }

    /// <summary>
    /// Gets a human-readable duration string in <c>h:mm:ss</c> or <c>m:ss</c> format.
    /// </summary>
    public string DurationDisplay => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    /// <summary>
    /// Gets a human-readable recorded-at string showing the local date and time.
    /// </summary>
    public string RecordedAtDisplay => RecordedAt.ToLocalTime().ToString("dd MMM yyyy  HH:mm");

    /// <summary>Gets a star-rating display string, e.g. <c>★★★☆☆</c>.</summary>
    public string RatingDisplay =>
        new string('★', Rating) + new string('☆', 5 - Rating);

    /// <summary>
    /// Initialises a new <see cref="ClipCardViewModel"/> from a domain <see cref="Clip"/> entity.
    /// </summary>
    /// <param name="clip">The domain clip entity to project.</param>
    /// <param name="stripFrameCount">
    /// The number of frames in the preview strip sprite-sheet, from <c>AppSettings.PreviewStripFrameCount</c>.
    /// </param>
    public ClipCardViewModel(Clip clip, int stripFrameCount = 20)
    {
        ClipId            = clip.Id;
        FilePath          = clip.FilePath;
        _fileName         = clip.FileName;
        ThumbnailPath     = clip.ThumbnailPath;
        StripPath         = clip.PreviewStripPath;
        StripFrameCount   = stripFrameCount;
        Duration          = clip.Duration;
        RecordedAt        = clip.CreatedAt;
        Status            = clip.Status;
        Rating            = clip.Rating;
        IsFavourite       = clip.IsFavourite;
        _isBroken         = clip.IsBroken;
        SuggestedGameName = clip.SuggestedGameName;

        var gameTag = clip.ClipTags.FirstOrDefault(ct => ct.Tag?.Type == TagType.Game);
        GameName         = gameTag?.Tag?.Name;
        GameTagId        = gameTag?.TagId;
        _gameCoverUrl    = gameTag?.Tag?.GameCoverUrl;
        _gameStoreAppId  = gameTag?.Tag?.GameStoreAppId;
        GeneralTagIds  = clip.ClipTags
            .Where(ct => ct.Tag?.Type == TagType.General)
            .Select(ct => ct.TagId)
            .ToList();
        PlayerTagIds   = clip.ClipPlayers
            .Where(cp => cp.Player is not null)
            .Select(cp => cp.PlayerId)
            .ToList();
        _playerIconPaths = clip.ClipPlayers
            .Where(cp => cp.Player is not null)
            .Select(cp => cp.Player.IconPath)
            .ToList();
        TagsDisplay    = string.Join(", ", clip.ClipTags
            .Where(ct => ct.Tag?.Type == TagType.General)
            .Select(ct => ct.Tag!.Name));
        PlayersDisplay = string.Join(", ", clip.ClipPlayers
            .Where(cp => cp.Player is not null)
            .Select(cp => cp.Player.DisplayName));

        QuickFilterByGameCommand = new RelayCommand(
            () => QuickFilterGameRequested?.Invoke(GameTagId!.Value),
            () => GameTagId.HasValue);
    }

    /// <summary>
    /// Asynchronously decodes the thumbnail image on a background thread and stores it in
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
            // Thumbnail decode failure is non-fatal; the card shows "No Preview" instead.
        }
    }

    /// <summary>
    /// Asynchronously decodes the preview strip sprite-sheet on a background thread and stores it in
    /// <see cref="StripBitmap"/>. Called lazily when the pointer first enters the card.
    /// </summary>
    public async Task LoadStripAsync()
    {
        if (string.IsNullOrEmpty(StripPath) || !File.Exists(StripPath))
            return;

        try
        {
            var bitmap = await Task.Run(() => new Bitmap(StripPath));
            StripBitmap = bitmap;
        }
        catch
        {
            // Strip decode failure is non-fatal; the card falls back to the static thumbnail.
        }
    }

    /// <summary>
    /// Asynchronously loads the game cover art from the local disk cache (or the Steam CDN when
    /// not yet cached) and loads any player icon bitmaps from their local file paths.
    /// Failures are silently ignored; bitmaps remain null.
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

        // Player icon bitmaps: one entry per player (null = no icon, renders as placeholder).
        var icons = new List<Bitmap?>();
        foreach (var iconPath in _playerIconPaths)
        {
            if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
            {
                icons.Add(null);
                continue;
            }
            try
            {
                var bmp = await Task.Run(() => new Bitmap(iconPath));
                icons.Add(bmp);
            }
            catch
            {
                // Icon decode failure is non-fatal; add placeholder for this player.
                icons.Add(null);
            }
        }
        PlayerIconBitmaps = icons;
    }
}
