using System;
using ClipStudio.UI.Parsing;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Material.Icons;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the clip detail and player view.
/// Owns a <see cref="LibVLCSharp.Shared.MediaPlayer"/> instance and exposes transport controls,
/// highlight management, tag management, rename, trim/export, keyboard shortcuts, and queue navigation.
/// Implements <see cref="IDisposable"/> to release the unmanaged MediaPlayer when the view is closed.
/// </summary>
public sealed partial class ClipDetailViewModel : ViewModelBase, IAudioPlaybackHost, IPlaybackHost, ITrimEditorHost, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly IClipService _clipService;
    private readonly IHighlightService _highlightService;
    private readonly ITagService _tagService;
    private readonly IExportService _exportService;
    private readonly ISettingsService _settingsService;
    private readonly IPlayerService _playerService;
    private readonly ClipStudio.Application.Interfaces.IGameTagAliasService _gameTagAliasService;
    private readonly ClipStudio.UI.Services.ISoundService _soundService;
    private readonly ClipStudio.Application.Interfaces.ITagSuggestionService _tagSuggestionService;
    private readonly ITranscriptionService _transcriptionService;
    private readonly ITranscriptionRepository _transcriptionRepository;

    /// <summary>Gets the transcription panel view model for the current clip.</summary>
    public TranscriptionViewModel Transcription { get; }

    /// <summary>Gets the child view model that owns frame capture for the open clip.</summary>
    public ScreenshotViewModel Screenshots { get; }

    /// <summary>Gets the child view model that owns per-clip audio routing and mixing.</summary>
    public AudioMixerViewModel Audio { get; }

    /// <summary>Gets the child view model that owns the playback transport.</summary>
    public PlaybackViewModel Playback { get; }

    /// <summary>Gets the child view model that owns the trim and export form.</summary>
    public TrimEditorViewModel Trim { get; }

    /// <summary>Gets or sets the SRT file path of the latest transcription, used for subtitle overlay.</summary>
    private string? _latestSrtPath;

    /// <summary>
    /// Gets a value indicating whether a transcription exists for the current clip,
    /// enabling the subtitle overlay toggle in the transport bar.
    /// </summary>
    [ObservableProperty] private bool _hasTranscription;

    /// <summary>Gets or sets whether the subtitle overlay is currently active.</summary>
    [ObservableProperty] private bool _isSubtitlesEnabled;

    /// <summary>Gets the command that toggles the subtitle overlay on or off.</summary>
    public IRelayCommand ToggleSubtitlesCommand { get; }

    private Clip? _clip;
    private Media? _media;




    /// <summary>
    /// When >= 0, the next <see cref="OnPlayerPlaying"/> callback should seek back to this
    /// position (ms) after a VLC media reload triggered by a mode transition.
    /// </summary>
    private long _mixReloadSeekMs = -1;



    /// <summary>
    /// VLC track ID to apply via <c>SetAudioTrack</c> immediately after the next media reload
    /// completes. -2 means no pending native track selection; -1 means mute (disable audio);
    /// any non-negative value is the track ID to select.
    /// </summary>
    private int _pendingNativeTrackId = -2;





    /// <summary>
    /// Indicates that the next <see cref="OnPlayerPlaying"/> callback should seek to
    /// <see cref="WatchStart"/> (used in watch mode to constrain playback to the highlight range).
    /// </summary>
    private bool _watchModeSeekPending;

    /// <summary>
    /// When true, the next <see cref="OnPlayerPlaying"/> callback should immediately pause
    /// and reset position to 0 (set after end-of-clip with loop off, so the user can replay).
    /// </summary>
    private bool _replayAfterEnd;

    /// <summary>
    /// When true, the next <see cref="OnPlayerPlaying"/> callback should seek to
    /// <see cref="WatchStart"/> and immediately pause (set when watch-mode LoopOff reaches the
    /// clip's natural end so the user can replay the highlight from its start).
    /// </summary>
    private bool _watchModeEndPending;


    /// <summary>Gets the pending tags to apply when the new highlight is saved.</summary>
    public ObservableCollection<TagChipViewModel> PendingHighlightTags { get; } = new();

    // ---- Player ----

    /// <summary>Gets the VLC media player driving this view.</summary>
    public MediaPlayer MediaPlayer { get; }

    /// <summary>Gets or sets the title shown in the header: the file name, or the highlight label in watch mode.</summary>
    [ObservableProperty] private string _clipTitle = string.Empty;

    /// <summary>
    /// The length the transport is showing: the clip's duration, or the watched highlight's.
    /// Kept here because the trim and highlight editors still clamp against it.
    /// </summary>
    private TimeSpan _clipDuration;

    // ---- Clip metadata ----

    /// <summary>Gets or sets the editable free-text notes for this clip.</summary>
    [ObservableProperty] private string _notes = string.Empty;

    /// <summary>Gets or sets the star rating (0 = unrated, 1-5).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRated1OrMore))]
    [NotifyPropertyChangedFor(nameof(IsRated2OrMore))]
    [NotifyPropertyChangedFor(nameof(IsRated3OrMore))]
    [NotifyPropertyChangedFor(nameof(IsRated4OrMore))]
    [NotifyPropertyChangedFor(nameof(IsRated5OrMore))]
    private int _rating;

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

    /// <summary>Gets or sets a value indicating whether the clip is marked as a favourite.</summary>
    [ObservableProperty] private bool _isFavourite;

    /// <summary>Gets or sets a value indicating whether this clip is still in the Unreviewed state.</summary>
    [ObservableProperty] private bool _isUnreviewed;

    // ---- Rename ----

    /// <summary>Gets or sets a value indicating whether the inline rename form is active.</summary>
    [ObservableProperty] private bool _isRenaming;

    /// <summary>Gets or sets the candidate name being edited in the rename text box.</summary>
    [ObservableProperty] private string _renameValue = string.Empty;

    /// <summary>
    /// Gets or sets the original filename from disk (the stem of <c>_clip.FilePath</c>).
    /// Shown in the rename form so the user can see what the file is named on disk.
    /// </summary>
    [ObservableProperty] private string _originalFileName = string.Empty;

    // ---- Tag management ----

    /// <summary>Gets the collection of general tags currently applied to the clip.</summary>
    public ObservableCollection<TagChipViewModel> ClipTags { get; } = new();

    /// <summary>
    /// Gets the list of suggested tags computed by <see cref="ClipStudio.Application.Interfaces.ITagSuggestionService"/>
    /// for the current clip. Populated after load; empty in watch mode.
    /// </summary>
    public ObservableCollection<TagSuggestionChipViewModel> TagSuggestions { get; } = new();

    /// <summary>Gets or sets the current game tag chip for this clip, or null if none is set.</summary>
    [ObservableProperty] private TagChipViewModel? _clipGameTag;

    /// <summary>Gets the list of available general tags for the add-tag picker.</summary>
    public ObservableCollection<Tag> AvailableGeneralTags { get; } = new();

    /// <summary>Gets the list of available game tags for the game-tag picker.</summary>
    public ObservableCollection<Tag> AvailableGameTags { get; } = new();

    /// <summary>Gets or sets the general tag selected in the picker (not yet applied).</summary>
    [ObservableProperty] private Tag? _selectedGeneralTag;

    /// <summary>Gets or sets the game tag selected in the picker (not yet applied).</summary>
    [ObservableProperty] private Tag? _selectedGameTag;

    /// <summary>
    /// Gets or sets a value indicating whether confirming a game tag on a clip that has a
    /// <c>SuggestedGameName</c> should create a remembered game-alias mapping.
    /// Defaults to true so future clips with the same OBS string are auto-tagged.
    /// </summary>
    [ObservableProperty] private bool _rememberGameAlias = true;

    /// <summary>
    /// Gets or sets the suggested game name parsed from the clip filename by the OBS script.
    /// Null when the clip has no suggestion or after the user has confirmed a game tag.
    /// Drives the visibility of the "Remember this association" toggle in the UI.
    /// </summary>
    [ObservableProperty] private string? _suggestedGameNameDisplay;

    // ---- Add-highlight form ----

    /// <summary>Gets or sets whether the add-highlight form is currently visible.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HighlightStartFraction))]
    [NotifyPropertyChangedFor(nameof(HighlightEndFraction))]
    private bool _isAddingHighlight;

    /// <summary>Gets or sets the label typed in the new-highlight form.</summary>
    [ObservableProperty] private string _newHighlightLabel = string.Empty;

    /// <summary>Gets or sets the formatted start-time string of the highlight being created.</summary>
    [ObservableProperty] private string _highlightStartDisplay = "0:00";

    /// <summary>Gets or sets the formatted end-time string of the highlight being created.</summary>
    [ObservableProperty] private string _highlightEndDisplay = "0:00";

    /// <summary>Gets or sets a validation error for the add-highlight form, or null if none.</summary>
    [ObservableProperty] private string? _highlightAddError;

    private TimeSpan _highlightStart = TimeSpan.Zero;
    private TimeSpan _highlightEnd   = TimeSpan.Zero;

    /// <summary>
    /// Gets the proportional position (0.0-1.0) of the highlight start handle within the clip.
    /// When editing an existing highlight, returns the fraction derived from <see cref="HighlightViewModel.EditStartDisplay"/>;
    /// when adding a new highlight, returns the fraction from the add-form's start time.
    /// </summary>
    public double HighlightStartFraction
    {
        get
        {
            if (_editingHighlight is not null)
                return Playback.DurationSeconds > 0 ? _editHighlightStart.TotalSeconds / Playback.DurationSeconds : 0;
            return Playback.DurationSeconds > 0 ? _highlightStart.TotalSeconds / Playback.DurationSeconds : 0;
        }
    }

    /// <summary>
    /// Gets the proportional position (0.0-1.0) of the highlight end handle within the clip.
    /// When editing an existing highlight, returns the fraction derived from <see cref="HighlightViewModel.EditEndDisplay"/>;
    /// when adding a new highlight, returns the fraction from the add-form's end time.
    /// </summary>
    public double HighlightEndFraction
    {
        get
        {
            if (_editingHighlight is not null)
                return Playback.DurationSeconds > 0 ? _editHighlightEnd.TotalSeconds / Playback.DurationSeconds : 0;
            return Playback.DurationSeconds > 0 ? _highlightEnd.TotalSeconds / Playback.DurationSeconds : 0;
        }
    }


    // ---- Export queue (shared with highlight export) ----

    /// <summary>Gets or sets whether an export job is currently being processed in the background.</summary>
    [ObservableProperty] private bool _isExporting;

    /// <summary>Gets or sets a status message shown while an export runs or after it completes.</summary>
    [ObservableProperty] private string? _exportStatusMessage;

    private bool _isProcessingExport;


    // ---- Highlight loop ----

    /// <summary>
    /// Gets or sets the highlight whose time range currently constrains playback (loop mode).
    /// When set, the player seeks back to <see cref="HighlightViewModel.StartTime"/> whenever
    /// the position reaches <see cref="HighlightViewModel.EndTime"/>.
    /// </summary>
    [ObservableProperty] private HighlightViewModel? _lockedHighlight;


    /// <summary>Gets or sets whether a previous clip in the navigation sequence exists.</summary>
    [ObservableProperty] private bool _hasPrevious;

    /// <summary>Gets or sets whether a next clip in the navigation sequence exists.</summary>
    [ObservableProperty] private bool _hasNext;

    /// <summary>Optional callback to navigate to the next clip in the queue sequence.</summary>
    public Action? NextClipRequested { get; set; }

    /// <summary>Optional callback to navigate to the previous clip in the queue sequence.</summary>
    public Action? PreviousClipRequested { get; set; }

    // ---- Highlight sequence navigation (watch mode) ----

    /// <summary>Gets or sets whether a previous highlight exists in the watch-mode sequence.</summary>
    [ObservableProperty] private bool _hasPreviousHighlight;

    /// <summary>Gets or sets whether a next highlight exists in the watch-mode sequence.</summary>
    [ObservableProperty] private bool _hasNextHighlight;

    /// <summary>Optional callback invoked when the user navigates to the previous highlight in watch mode.</summary>
    public Action? PreviousHighlightRequested { get; set; }

    /// <summary>Optional callback invoked when the user navigates to the next highlight in watch mode.</summary>
    public Action? NextHighlightRequested { get; set; }

    // ---- Player tagging ----

    /// <summary>Gets the chips representing players currently tagged on this clip.</summary>
    public ObservableCollection<TagChipViewModel> ClipPlayerChips { get; } = new();

    /// <summary>Gets the list of all known players, used to populate the player picker.</summary>
    public ObservableCollection<Player> AvailablePlayers { get; } = new();

    /// <summary>Gets or sets the player selected in the picker (not yet applied).</summary>
    [ObservableProperty] private Player? _selectedPlayer;


    // ---- Collections ----

    /// <summary>Gets the highlights associated with the current clip.</summary>
    public ObservableCollection<HighlightViewModel> Highlights { get; } = new();

    /// <summary>
    /// The highlight currently open in inline-edit mode, or <c>null</c> when none is being edited.
    /// Drives dual-mode behaviour of the timeline handles and mark-start/end commands.
    /// </summary>
    private HighlightViewModel? _editingHighlight;

    /// <summary>
    /// Precise start time for the highlight currently being edited.
    /// Avoids string-parse round-trips when computing <see cref="HighlightStartFraction"/>.
    /// </summary>
    private TimeSpan _editHighlightStart;

    /// <summary>
    /// Precise end time for the highlight currently being edited.
    /// Avoids string-parse round-trips when computing <see cref="HighlightEndFraction"/>.
    /// </summary>
    private TimeSpan _editHighlightEnd;

    /// <summary>
    /// Gets a value indicating whether the timeline handle canvas should be visible.
    /// True when adding a new highlight or editing an existing one.
    /// </summary>
    public bool IsAddingOrEditingHighlight => IsAddingHighlight || _editingHighlight is not null;

    /// <summary>
    /// Callback invoked whenever a highlight is created, updated, or deleted on the current clip.
    /// Wired by <see cref="MainWindowViewModel"/> to reload the Highlights page so it stays in sync.
    /// </summary>
    public Action? HighlightsChanged { get; set; }

    // ---- Watch mode (highlights player) ----

    /// <summary>
    /// Gets or sets a value indicating whether this view is operating in watch-only mode
    /// for a specific highlight time range. When true the right-side editing panel is hidden
    /// and playback is constrained between <see cref="WatchStart"/> and <see cref="WatchEnd"/>.
    /// Must be set before calling <see cref="LoadAsync"/>.
    /// </summary>
    public bool IsWatchMode { get; set; }

    /// <summary>Gets or sets the start time of the highlight range in watch mode.</summary>
    public TimeSpan WatchStart { get; set; }

    /// <summary>Gets or sets the end time of the highlight range in watch mode.</summary>
    public TimeSpan WatchEnd { get; set; }

    /// <summary>
    /// Gets or sets the label of the highlight shown in watch mode.
    /// When set, this is displayed in the header instead of the clip file name.
    /// </summary>
    public string? WatchHighlightLabel { get; set; }

    /// <summary>
    /// Gets or sets the database ID of the highlight currently being watched.
    /// Set by <see cref="MainWindowViewModel"/> when entering watch mode.
    /// Null when not in watch mode or when watching a range without an ID.
    /// </summary>
    public int? WatchHighlightId { get; set; }

    /// <summary>Gets or sets the star rating (0-5) of the watched highlight.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchIsRated1OrMore))]
    [NotifyPropertyChangedFor(nameof(WatchIsRated2OrMore))]
    [NotifyPropertyChangedFor(nameof(WatchIsRated3OrMore))]
    [NotifyPropertyChangedFor(nameof(WatchIsRated4OrMore))]
    [NotifyPropertyChangedFor(nameof(WatchIsRated5OrMore))]
    private int _watchHighlightRating;

    /// <summary>Gets or sets a value indicating whether the watched highlight is marked as a favourite.</summary>
    [ObservableProperty] private bool _watchHighlightIsFavorite;

    /// <summary>Gets whether the watched highlight rating is at least 1.</summary>
    public bool WatchIsRated1OrMore => WatchHighlightRating >= 1;

    /// <summary>Gets whether the watched highlight rating is at least 2.</summary>
    public bool WatchIsRated2OrMore => WatchHighlightRating >= 2;

    /// <summary>Gets whether the watched highlight rating is at least 3.</summary>
    public bool WatchIsRated3OrMore => WatchHighlightRating >= 3;

    /// <summary>Gets whether the watched highlight rating is at least 4.</summary>
    public bool WatchIsRated4OrMore => WatchHighlightRating >= 4;

    /// <summary>Gets whether the watched highlight rating is at least 5.</summary>
    public bool WatchIsRated5OrMore => WatchHighlightRating >= 5;

    /// <summary>Gets the command that sets the star rating of the currently watched highlight.</summary>
    public IRelayCommand SetWatchRatingCommand { get; private set; } = null!;

    /// <summary>Gets the command that toggles the favourite state of the currently watched highlight.</summary>
    public IRelayCommand ToggleWatchFavoriteCommand { get; private set; } = null!;

    /// <summary>
    /// Gets the command that opens the original (full) clip from watch mode.
    /// Set by <see cref="MainWindowViewModel"/> after construction.
    /// </summary>
    public IRelayCommand? OpenOriginalClipCommand { get; set; }

    // ---- Delete / trash ----

    /// <summary>Gets or sets a value indicating whether the delete confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isDeleteConfirmVisible;

    /// <summary>Gets the command that requests deletion of the current clip.</summary>
    public IRelayCommand DeleteClipCommand { get; private set; } = null!;

    /// <summary>Gets the command that confirms the pending clip deletion.</summary>
    public IAsyncRelayCommand ConfirmDeleteCommand { get; private set; } = null!;

    /// <summary>Gets the command that cancels the pending clip deletion.</summary>
    public IRelayCommand CancelDeleteCommand { get; private set; } = null!;

    // ---- Navigation callback ----

    /// <summary>
    /// Callback set by <see cref="MainWindowViewModel"/> to close this detail view and
    /// navigate back to the previous page.
    /// </summary>
    public Action? BackRequested { get; set; }

    /// <summary>
    /// Callback invoked whenever the clip's status changes (reviewed or trashed).
    /// Wired by <see cref="MainWindowViewModel"/> to refresh the unreviewed badge count.
    /// </summary>
    public Action? ClipStatusChanged { get; set; }

    /// <summary>
    /// Callback invoked after a successful rename, passing (clipId, newFileName).
    /// Wired by <see cref="MainWindowViewModel"/> to update the clip name in the library grid.
    /// </summary>
    public Action<int, string>? ClipRenamed { get; set; }

    // ---- Commands ----

    /// <summary>Gets the command that closes this view and returns to the library/queue.</summary>
    public IRelayCommand BackCommand { get; }

    /// <summary>Gets the command that shows the add-highlight inline form.</summary>
    public IRelayCommand BeginAddHighlightCommand { get; }

    /// <summary>Gets the command that dismisses the add-highlight form without saving.</summary>
    public IRelayCommand CancelAddHighlightCommand { get; }

    /// <summary>Gets the command that sets the highlight start time to the current playback position.</summary>
    public IRelayCommand MarkHighlightStartCommand { get; }

    /// <summary>Gets the command that sets the highlight end time to the current playback position.</summary>
    public IRelayCommand MarkHighlightEndCommand { get; }

    /// <summary>Gets the command that saves the new highlight to the database.</summary>
    public IAsyncRelayCommand SaveHighlightCommand { get; }

    /// <summary>Gets the command that persists the current notes text to the database.</summary>
    public IAsyncRelayCommand SaveNotesCommand { get; }

    /// <summary>Gets the command that transitions the clip status from Unreviewed to Reviewed.</summary>
    public IAsyncRelayCommand MarkAsReviewedCommand { get; }

    /// <summary>Gets the command that toggles the clip's favourite flag.</summary>
    public IAsyncRelayCommand ToggleFavouriteCommand { get; }

    /// <summary>Gets the command that applies the currently selected general tag to the clip.</summary>
    public IAsyncRelayCommand AddGeneralTagCommand { get; }

    /// <summary>Gets the command that applies the currently selected game tag to the clip.</summary>
    public IAsyncRelayCommand AddGameTagCommand { get; }

    /// <summary>Gets the command that enters the inline rename mode.</summary>
    public IRelayCommand BeginRenameCommand { get; }

    /// <summary>Gets the command that commits the pending rename.</summary>
    public IAsyncRelayCommand ConfirmRenameCommand { get; }

    /// <summary>Gets the command that cancels the rename without saving.</summary>
    public IRelayCommand CancelRenameCommand { get; }

    /// <summary>Gets the command that restores the rename textbox to the original filename on disk.</summary>
    public IRelayCommand RestoreOriginalNameCommand { get; }

    /// <summary>Gets the command that clears the export status bar.</summary>
    public IRelayCommand DismissExportStatusCommand { get; }

    /// <summary>Gets the command that releases the active highlight loop lock.</summary>
    public IRelayCommand UnlockHighlightCommand { get; }

    /// <summary>Gets the command that navigates to the previous clip in the queue.</summary>
    public IRelayCommand PreviousCommand { get; }

    /// <summary>Gets the command that navigates to the next clip in the queue.</summary>
    public IRelayCommand NextCommand { get; }

    /// <summary>Gets the command that navigates to the previous highlight in the watch-mode sequence.</summary>
    public IRelayCommand NavigatePreviousHighlightCommand { get; }

    /// <summary>Gets the command that navigates to the next highlight in the watch-mode sequence.</summary>
    public IRelayCommand NavigateNextHighlightCommand { get; }

    /// <summary>
    /// Gets the command that reveals the current clip's file in the operating-system file explorer.
    /// On Windows opens Explorer with the file selected; on macOS uses <c>open -R</c>;
    /// on Linux uses <c>xdg-open</c> on the containing directory.
    /// </summary>
    public IRelayCommand OpenInExplorerCommand { get; }

    /// <summary>Gets the command that tags the selected player on the current clip.</summary>
    public IAsyncRelayCommand AddPlayerCommand { get; }

    /// <summary>Gets the command that shows the clear-all-tags confirmation strip.</summary>
    public IRelayCommand ShowClearAllTagsConfirmCommand { get; }

    /// <summary>Gets the command that confirms removing all directly-applied general tags from this clip.</summary>
    public IAsyncRelayCommand ConfirmClearAllTagsCommand { get; }

    /// <summary>Gets the command that cancels the clear-all-tags confirmation.</summary>
    public IRelayCommand CancelClearAllTagsConfirmCommand { get; }

    /// <summary>Gets or sets whether the clear-all-tags confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isClearAllTagsConfirmVisible;

    /// <summary>Gets the command that shows the clear-all-players confirmation strip.</summary>
    public IRelayCommand ShowClearAllPlayersConfirmCommand { get; }

    /// <summary>Gets the command that confirms removing all players tagged on this clip.</summary>
    public IAsyncRelayCommand ConfirmClearAllPlayersCommand { get; }

    /// <summary>Gets the command that cancels the clear-all-players confirmation.</summary>
    public IRelayCommand CancelClearAllPlayersConfirmCommand { get; }

    /// <summary>Gets or sets whether the clear-all-players confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isClearAllPlayersConfirmVisible;

    /// <summary>Gets the command that shows the clear-all-data confirmation strip.</summary>
    public IRelayCommand ShowClearAllDataConfirmCommand { get; }

    /// <summary>Gets the command that resets the clip to Unreviewed and removes all tags, players, rating and favourite.</summary>
    public IAsyncRelayCommand ConfirmClearAllDataCommand { get; }

    /// <summary>Gets the command that cancels the clear-all-data confirmation.</summary>
    public IRelayCommand CancelClearAllDataConfirmCommand { get; }

    /// <summary>Gets or sets whether the clear-all-data confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isClearAllDataConfirmVisible;

    /// <summary>
    /// Gets the command that sets the star rating of the current clip.
    /// Accepts a string parameter ("1"–"5") corresponding to the star clicked by the user.
    /// The string is parsed to an integer internally; Avalonia passes CommandParameter literals as strings.
    /// </summary>
    public IAsyncRelayCommand<string> SetRatingCommand { get; }

    /// <summary>
    /// Gets the command that accepts a tag suggestion and applies the suggested tag to the current clip.
    /// Accepts the tag ID as an <see cref="int"/> parameter.
    /// </summary>
    public IAsyncRelayCommand<int> AcceptSuggestionCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="ClipDetailViewModel"/> and creates the underlying
    /// <see cref="LibVLCSharp.Shared.MediaPlayer"/>.
    /// </summary>
    public ClipDetailViewModel(
        LibVLC libVlc,
        IClipService clipService,
        IHighlightService highlightService,
        IScreenshotService screenshotService,
        ITagService tagService,
        IExportService exportService,
        ISettingsService settingsService,
        IPlayerService playerService,
        IAudioTrackService audioTrackService,
        ClipStudio.Application.Interfaces.IMixedAudioService mixedAudioService,
        ClipStudio.Application.Interfaces.IFileSystem fileSystem,
        ClipStudio.Application.Models.AppDataPaths appDataPaths,
        ClipStudio.Application.Interfaces.IGameTagAliasService gameTagAliasService,
        ClipStudio.UI.Services.ISoundService soundService,
        ClipStudio.Application.Interfaces.ITagSuggestionService tagSuggestionService,
        ITranscriptionService transcriptionService,
        ITranscriptionRepository transcriptionRepository)
    {
        _libVlc                   = libVlc;
        _clipService              = clipService;
        _highlightService         = highlightService;
        _tagService               = tagService;
        _exportService            = exportService;
        _settingsService          = settingsService;
        _playerService            = playerService;
        _gameTagAliasService      = gameTagAliasService;
        _soundService             = soundService;
        _tagSuggestionService     = tagSuggestionService;
        _transcriptionService     = transcriptionService;
        _transcriptionRepository  = transcriptionRepository;

        Transcription = new TranscriptionViewModel(
            _transcriptionService,
            _transcriptionRepository,
            _settingsService,
            seekMs => SeekToMs(seekMs));
        Transcription.TranscriptionCompleted += OnTranscriptionCompleted;
        Transcription.SegmentTextEdited      += OnSegmentTextEdited;

        MediaPlayer = new MediaPlayer(_libVlc);
        // The explicit initial volume is applied by AudioMixerViewModel's constructor below,
        // which owns the master level. Without it LibVLC keeps whatever the system VLC config
        // (vlcrc) supplies, which can be 0.
        MediaPlayer.TimeChanged += OnPlayerTimeChanged;
        MediaPlayer.Playing     += OnPlayerPlaying;
        MediaPlayer.Paused      += OnPlayerPaused;
        MediaPlayer.Stopped     += OnPlayerStopped;
        MediaPlayer.EndReached  += OnPlayerEndReached;

        // Constructed after MediaPlayer so the host seam and position delegate see a real player.
        Screenshots = new ScreenshotViewModel(
            screenshotService,
            () => TimeSpan.FromMilliseconds(MediaPlayer.Time));

        Audio = new AudioMixerViewModel(
            this,
            audioTrackService,
            mixedAudioService,
            settingsService,
            fileSystem,
            appDataPaths);

        Audio.TracksRefreshed += names => Transcription.SetAvailableTracks(names);

        // Trim first: the transport's precision delegate reads Trim.IsTrimming.
        Trim = new TrimEditorViewModel(this, exportService, settingsService);

        Playback = new PlaybackViewModel(
            this,
            () => IsAddingOrEditingHighlight || Trim.IsTrimming);

        BackCommand               = new RelayCommand(() => BackRequested?.Invoke());
        BeginAddHighlightCommand  = new RelayCommand(BeginAddHighlight);
        CancelAddHighlightCommand = new RelayCommand(ResetHighlightForm);
        MarkHighlightStartCommand = new RelayCommand(MarkHighlightStart);
        MarkHighlightEndCommand   = new RelayCommand(MarkHighlightEnd);
        SaveHighlightCommand      = new AsyncRelayCommand(SaveHighlightAsync);
        SaveNotesCommand          = new AsyncRelayCommand(SaveNotesAsync);
        MarkAsReviewedCommand     = new AsyncRelayCommand(MarkAsReviewedAsync);
        ToggleFavouriteCommand    = new AsyncRelayCommand(ToggleFavouriteAsync);
        AddGeneralTagCommand      = new AsyncRelayCommand(AddGeneralTagAsync);
        AddGameTagCommand         = new AsyncRelayCommand(AddGameTagAsync);
        BeginRenameCommand        = new RelayCommand(() => { RenameValue = ClipTitle; IsRenaming = true; });
        ConfirmRenameCommand      = new AsyncRelayCommand(ConfirmRenameAsync);
        CancelRenameCommand       = new RelayCommand(() => IsRenaming = false);
        RestoreOriginalNameCommand   = new RelayCommand(() => RenameValue = OriginalFileName);
        DismissExportStatusCommand      = new RelayCommand(() => ExportStatusMessage = null);
        UnlockHighlightCommand    = new RelayCommand(() => LockedHighlight = null);
        PreviousCommand                   = new RelayCommand(() => PreviousClipRequested?.Invoke());
        NextCommand                       = new RelayCommand(() => NextClipRequested?.Invoke());
        NavigatePreviousHighlightCommand  = new RelayCommand(() => PreviousHighlightRequested?.Invoke());
        NavigateNextHighlightCommand      = new RelayCommand(() => NextHighlightRequested?.Invoke());
        AddPlayerCommand                   = new AsyncRelayCommand(AddPlayerAsync);
        ShowClearAllTagsConfirmCommand     = new RelayCommand(() => IsClearAllTagsConfirmVisible    = true);
        ConfirmClearAllTagsCommand         = new AsyncRelayCommand(ClearAllTagsAsync);
        CancelClearAllTagsConfirmCommand   = new RelayCommand(() => IsClearAllTagsConfirmVisible    = false);
        ShowClearAllPlayersConfirmCommand   = new RelayCommand(() => IsClearAllPlayersConfirmVisible = true);
        ConfirmClearAllPlayersCommand       = new AsyncRelayCommand(ClearAllPlayersAsync);
        CancelClearAllPlayersConfirmCommand = new RelayCommand(() => IsClearAllPlayersConfirmVisible = false);
        SetRatingCommand          = new AsyncRelayCommand<string>(s => SetRatingAsync(int.TryParse(s, out var r) ? r : 0));
        AcceptSuggestionCommand   = new AsyncRelayCommand<int>(AcceptSuggestionAsync);
        SetWatchRatingCommand     = new RelayCommand<string>(s => _ = SetWatchRatingAsync(int.TryParse(s, out var r) ? r : 0));
        ToggleWatchFavoriteCommand = new RelayCommand(() => _ = ToggleWatchFavoriteAsync());
        DeleteClipCommand         = new RelayCommand(() => IsDeleteConfirmVisible = true);
        ConfirmDeleteCommand      = new AsyncRelayCommand(ConfirmDeleteAsync);
        CancelDeleteCommand       = new RelayCommand(() => IsDeleteConfirmVisible = false);
        OpenInExplorerCommand     = new RelayCommand(OpenInExplorer);
        ShowClearAllDataConfirmCommand   = new RelayCommand(() => IsClearAllDataConfirmVisible   = true);
        ConfirmClearAllDataCommand       = new AsyncRelayCommand(ClearAllDataAsync);
        CancelClearAllDataConfirmCommand = new RelayCommand(() => IsClearAllDataConfirmVisible   = false);
        ToggleSubtitlesCommand           = new RelayCommand(ToggleSubtitles);
    }

    // ---- Load ----

    /// <summary>
    /// Asynchronously loads the clip with the given identifier into the player and populates all
    /// display properties, highlight collection, and tag collections.
    /// </summary>
    /// <param name="clipId">The database identifier of the clip to open.</param>
    public async Task LoadAsync(int clipId)
    {
        // Dispose previous media wrapper before creating a new one.
        _media?.Dispose();
        _media = null;

        _clip = await _clipService.GetByIdAsync(clipId);
        Screenshots.SetClip(_clip?.Id);
        if (_clip is null)
            return;

        ClipTitle    = (IsWatchMode && !string.IsNullOrEmpty(WatchHighlightLabel))
            ? WatchHighlightLabel
            : _clip.FileName;
        OriginalFileName         = System.IO.Path.GetFileNameWithoutExtension(_clip.FilePath);
        Notes                    = _clip.Notes ?? string.Empty;
        Rating                   = _clip.Rating;
        IsFavourite              = _clip.IsFavourite;
        IsUnreviewed             = _clip.Status == ClipStatus.Unreviewed;
        SuggestedGameNameDisplay = _clip.SuggestedGameName;

        Playback.Reset();
        Trim.Reset();
        if (IsWatchMode)
        {
            var watchDuration = WatchEnd - WatchStart;
            _clipDuration = watchDuration;
            Playback.SetWindow(WatchStart, WatchEnd);
        }
        else
        {
            _clipDuration = _clip.Duration;
            Playback.SetWindow(null, null);
        }
        Playback.SetDuration(_clipDuration);

        // Reset audio so the new clip's tracks are rediscovered on its first play.
        Audio.SetClip(_clip);
        _mixReloadSeekMs      = -1;
        _pendingNativeTrackId = -2;

        _media = new Media(_libVlc, _clip.FilePath, FromType.FromPath);
        MediaPlayer.Media = _media;

        if (!IsWatchMode)
        {
            await RefreshHighlightsAsync();
            RefreshTags();
            await LoadTagPickersAsync();
            await RefreshPlayersAsync();
            await LoadPlayerPickerAsync();
            _ = _clipService.IncrementPlayCountAsync(clipId);
            _ = LoadTagSuggestionsAsync(clipId);

            // Load any existing transcription for this clip.
            var trackNames = Audio.AudioTracks.Select(t => t.DisplayName).ToList();
            await Transcription.LoadAsync(clipId, trackNames);
            HasTranscription  = Transcription.HasExistingTranscription;
            _latestSrtPath    = Transcription.HasExistingTranscription
                ? (await _transcriptionRepository.GetLatestByClipIdAsync(clipId))?.SrtFilePath
                : null;
        }

        if (IsWatchMode || _settingsService.Current.AutoPlayOnOpen)
        {
            _watchModeSeekPending = IsWatchMode;
            MediaPlayer.Play();
        }
    }

    // ---- Prepare for close ----

    /// <summary>
    /// Stops playback and clears the media reference so the native VideoView can safely detach
    /// before this view model is disposed. Must be called before removing the view from the visual tree.
    /// </summary>
    public void PrepareForClose()
    {
        // Cancel any in-flight FFmpeg generation before stopping the player or deleting files.
        Audio.CancelPendingWork();

        Screenshots.SetClip(null);

        if (MediaPlayer.IsPlaying)
            MediaPlayer.Stop();

        MediaPlayer.Media = null;
        _media?.Dispose();
        _media = null;

        // When caching is disabled the mix files are treated as session-only temp files.
        Audio.DeleteSessionPreviews();
    }

    // ---- IAudioPlaybackHost ----

    /// <summary>
    /// Saves the current playback position, rebuilds the VLC <see cref="Media"/> from
    /// <paramref name="path"/>, and resumes playback. The saved position is reapplied on the next
    /// <c>Playing</c> event by <see cref="OnPlayerPlaying"/>, which is also where
    /// <paramref name="pendingNativeTrackId"/> is applied: the track cannot be selected until VLC
    /// reports the new media as playing.
    /// </summary>
    /// <param name="path">
    /// The absolute path of the media to open - either the original clip or a remux preview MKV.
    /// </param>
    /// <param name="pendingNativeTrackId">
    /// The audio track to select once the reload completes, or -2 for none.
    /// </param>
    public void ReloadMedia(string path, int pendingNativeTrackId = -2)
    {
        // Clamp to 0: MediaPlayer.Time returns -1 when no media is playing.
        _mixReloadSeekMs      = Math.Max(0, MediaPlayer.Time);
        _pendingNativeTrackId = pendingNativeTrackId;

        _media?.Dispose();
        _media = new Media(_libVlc, path, FromType.FromPath);

        MediaPlayer.Media = _media;
        MediaPlayer.Play();
    }

    /// <inheritdoc/>
    int IAudioPlaybackHost.Volume
    {
        get => MediaPlayer.Volume;
        set => MediaPlayer.Volume = value;
    }

    /// <inheritdoc/>
    public void SetAudioTrack(int trackId) => MediaPlayer.SetAudioTrack(trackId);

    // ---- ITrimEditorHost ----

    /// <inheritdoc/>
    Clip? ITrimEditorHost.CurrentClip => _clip;

    /// <inheritdoc/>
    bool ITrimEditorHost.CanBeginTrim => !IsWatchMode;

    /// <inheritdoc/>
    void ITrimEditorHost.PrepareForTrim()
    {
        if (IsAddingHighlight) ResetHighlightForm();
    }

    /// <inheritdoc/>
    double ITrimEditorHost.CurrentPositionSeconds => Playback.PositionSeconds;

    /// <inheritdoc/>
    double ITrimEditorHost.DurationSeconds => Playback.DurationSeconds;

    /// <inheritdoc/>
    IReadOnlyList<HighlightViewModel> ITrimEditorHost.Highlights => Highlights;

    /// <inheritdoc/>
    void ITrimEditorHost.OnTrimmingChanged() => Playback.RefreshDurationDisplay();

    /// <inheritdoc/>
    void ITrimEditorHost.RunExportQueue() => _ = RunExportQueueAsync();

    /// <inheritdoc/>
    void ITrimEditorHost.ReportExportFailure(string message) => ExportStatusMessage = message;

    // ---- IPlaybackHost ----

    /// <inheritdoc/>
    long IPlaybackHost.TimeMs
    {
        get => MediaPlayer.Time;
        set => MediaPlayer.Time = value;
    }

    /// <inheritdoc/>
    bool IPlaybackHost.IsPlayerPlaying => MediaPlayer.IsPlaying;

    /// <inheritdoc/>
    float IPlaybackHost.Fps => MediaPlayer.Fps;

    /// <inheritdoc/>
    void IPlaybackHost.Play() => MediaPlayer.Play();

    /// <inheritdoc/>
    void IPlaybackHost.Pause() => MediaPlayer.Pause();

    /// <inheritdoc/>
    public IReadOnlyList<AudioTrackDescriptor> GetAudioTracks()
        => MediaPlayer.AudioTrackDescription
            // Id -1 is the player's "Disabled" sentinel; it is not a real track.
            .Where(d => d.Id >= 0)
            .Select(d => new AudioTrackDescriptor(d.Id, d.Name ?? $"Track {d.Id}"))
            .ToList();

    // ---- Scrub support (called from code-behind) ----

    // ---- Private helpers ----

    private async Task RefreshHighlightsAsync()
    {
        if (_clip is null) return;

        // Clear editing state before destroying the old VMs.
        if (_editingHighlight is not null)
        {
            _editingHighlight.PropertyChanged -= OnEditingHighlightPropertyChanged;
            _editingHighlight = null;
            OnPropertyChanged(nameof(IsAddingOrEditingHighlight));
            OnPropertyChanged(nameof(HighlightStartFraction));
            OnPropertyChanged(nameof(HighlightEndFraction));
        }

        var lockedId = LockedHighlight?.HighlightId;

        var highlights = await _highlightService.GetByClipAsync(_clip.Id);
        Highlights.Clear();
        foreach (var h in highlights)
        {
            Highlights.Add(new HighlightViewModel(
                h,
                _clip.Duration,
                JumpToHighlight,
                DeleteHighlightAsync,
                AvailableGeneralTags,
                AddTagToHighlightAsync,
                RemoveTagFromHighlightAsync,
                ExportHighlightAsync,
                UpdateHighlightLabelAsync,
                onSetRating:        async (hvm, r) => await _highlightService.SetRatingAsync(hvm.HighlightId, r),
                onToggleFavorite:   async (hvm)    => await _highlightService.ToggleFavoriteAsync(hvm.HighlightId),
                onEditingChanged:   OnHighlightEditingChanged,
                getPlayerPosition:  () => TimeSpan.FromSeconds(Playback.PositionSeconds)));
        }

        // Re-apply locked state to the refreshed view models.
        if (lockedId.HasValue)
            LockedHighlight = Highlights.FirstOrDefault(h => h.HighlightId == lockedId.Value);
    }

    /// <summary>
    /// Called when a <see cref="HighlightViewModel"/> enters or exits inline edit mode.
    /// Tracks the editing highlight so that the timeline handles and mark commands route correctly.
    /// </summary>
    private void OnHighlightEditingChanged(HighlightViewModel hvm, bool isEditing)
    {
        if (isEditing)
        {
            _editingHighlight   = hvm;
            _editHighlightStart = hvm.StartTime;
            _editHighlightEnd   = hvm.EndTime;
            hvm.PropertyChanged += OnEditingHighlightPropertyChanged;
        }
        else if (_editingHighlight == hvm)
        {
            hvm.PropertyChanged -= OnEditingHighlightPropertyChanged;
            _editingHighlight = null;
        }

        OnPropertyChanged(nameof(IsAddingOrEditingHighlight));
        OnPropertyChanged(nameof(HighlightStartFraction));
        OnPropertyChanged(nameof(HighlightEndFraction));
        Playback.RefreshDurationDisplay();
    }

    /// <summary>
    /// Forwards handle-position updates to the view when the editing highlight's
    /// start or end display string changes (e.g. after a mark-start/end button press).
    /// </summary>
    private void OnEditingHighlightPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HighlightViewModel.EditStartDisplay))
        {
            if (TimestampInput.TryParse(_editingHighlight!.EditStartDisplay, out var t))
                _editHighlightStart = t;
            OnPropertyChanged(nameof(HighlightStartFraction));
        }
        else if (e.PropertyName == nameof(HighlightViewModel.EditEndDisplay))
        {
            if (TimestampInput.TryParse(_editingHighlight!.EditEndDisplay, out var t))
                _editHighlightEnd = t;
            OnPropertyChanged(nameof(HighlightEndFraction));
        }
    }

    /// <summary>Called by the source generator when <see cref="IsAddingHighlight"/> changes.</summary>
    partial void OnIsAddingHighlightChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAddingOrEditingHighlight));
        Playback.RefreshDurationDisplay();
    }

    /// <summary>
    /// Called by the source generator when <see cref="HighlightStartDisplay"/> changes.
    /// Parses the typed value back into <see cref="_highlightStart"/> when in add mode.
    /// </summary>
    partial void OnHighlightStartDisplayChanged(string value)
    {
        if (!IsAddingHighlight) return;
        if (TimestampInput.TryParse(value, out var t))
        {
            _highlightStart = t;
            OnPropertyChanged(nameof(HighlightStartFraction));
        }
    }

    /// <summary>
    /// Called by the source generator when <see cref="HighlightEndDisplay"/> changes.
    /// Parses the typed value back into <see cref="_highlightEnd"/> when in add mode.
    /// </summary>
    partial void OnHighlightEndDisplayChanged(string value)
    {
        if (!IsAddingHighlight) return;
        if (TimestampInput.TryParse(value, out var t))
        {
            _highlightEnd = t;
            OnPropertyChanged(nameof(HighlightEndFraction));
        }
    }

    private async Task UpdateHighlightLabelAsync(HighlightViewModel hvm, string newLabel, TimeSpan newStart, TimeSpan newEnd)
    {
        await _highlightService.UpdateAsync(hvm.HighlightId, newStart, newEnd, newLabel, null);
        // Rebuild the highlights list from DB so the renamed label is immediately visible.
        await RefreshHighlightsAsync();
        HighlightsChanged?.Invoke();
    }

    private async Task ExportHighlightAsync(HighlightViewModel highlight)
    {
        if (_clip is null) return;

        var dir   = Path.GetDirectoryName(_clip.FilePath) ?? string.Empty;
        var label = string.IsNullOrWhiteSpace(highlight.Label) || highlight.Label == "(unlabelled)"
            ? $"hl{highlight.HighlightId}"
            : highlight.Label.Replace(" ", "_");
        var outputPath = Path.Combine(dir,
            $"{Path.GetFileNameWithoutExtension(_clip.FileName)}_{label}.mp4");

        var settings = _settingsService.Current;
        try
        {
            await _exportService.QueueAsync(
                _clip.Id,
                highlight.HighlightId,
                outputPath,
                settings.DefaultTrimMode,
                settings.DeleteOriginalAfterDestructiveTrim);
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Export failed: {ex.Message}";
            return;
        }

        _ = RunExportQueueAsync();
    }

    private async Task LoadTagSuggestionsAsync(int clipId)
    {
        try
        {
            var suggestions = await _tagSuggestionService.GetSuggestionsAsync(clipId);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                TagSuggestions.Clear();
                foreach (var s in suggestions)
                    TagSuggestions.Add(new TagSuggestionChipViewModel(s, AcceptSuggestionAsync));
            });
        }
        catch
        {
            // Non-fatal: suggestions are a convenience feature.
        }
    }

    private async Task AcceptSuggestionAsync(int tagId)
    {
        if (_clip is null) return;

        var tag = await _tagService.GetByIdAsync(tagId);
        if (tag is null) return;

        await _clipService.AddTagAsync(_clip.Id, tag.Id);
        _clip = await _clipService.GetByIdAsync(_clip.Id);
        RefreshTags();

        // Remove accepted suggestion from the list.
        var accepted = TagSuggestions.FirstOrDefault(s => s.TagId == tagId);
        if (accepted is not null)
            TagSuggestions.Remove(accepted);

        await AutoMarkReviewedIfEnabledAsync();
    }

    private void RefreshTags()
    {
        if (_clip is null) return;

        ClipTags.Clear();
        ClipGameTag = null;

        // A clip tag is LOCKED whenever any of the clip's highlights currently holds the same tag.
        // This is true regardless of whether the tag was on the clip before the highlight added it.
        // The user must remove the tag from every highlight that has it before they can remove it
        // from the clip. Propagation guarantees the tag is already in ClipTags when a highlight has it.
        var lockedTagIds = _clip.Highlights
            .SelectMany(h => h.HighlightTags)
            .Select(ht => ht.TagId)
            .ToHashSet();

        foreach (var ct in _clip.ClipTags.Where(ct => ct.Tag?.Type == TagType.General))
        {
            var tagId    = ct.TagId;
            var tagName  = ct.Tag!.Name;
            var isLocked = lockedTagIds.Contains(tagId);
            ClipTags.Add(new TagChipViewModel(tagId, tagName, RemoveClipTagAsync, isLocked));
        }

        var gameTag = _clip.ClipTags.FirstOrDefault(ct => ct.Tag?.Type == TagType.Game);
        if (gameTag?.Tag is not null)
        {
            var tagId    = gameTag.TagId;
            var tagName  = gameTag.Tag.Name;
            var isLocked = lockedTagIds.Contains(tagId);
            ClipGameTag = new TagChipViewModel(tagId, tagName, RemoveClipGameTagAsync, isLocked);
        }
    }

    /// <summary>
    /// Reloads the clip entity from the database (to get fresh ClipTags and HighlightTags)
    /// and refreshes the tag chip collection. Called after any highlight tag change so that
    /// the lock state of clip-level chips updates immediately.
    /// </summary>
    private async Task RefreshClipAndTagsAsync()
    {
        if (_clip is null) return;
        _clip = await _clipService.GetByIdAsync(_clip.Id);
        RefreshTags();
    }

    private async Task LoadTagPickersAsync()
    {
        var generalTags = await _tagService.GetByTypeAsync(TagType.General);
        AvailableGeneralTags.Clear();
        foreach (var t in generalTags)
            AvailableGeneralTags.Add(t);

        var gameTags = await _tagService.GetByTypeAsync(TagType.Game);
        AvailableGameTags.Clear();
        foreach (var t in gameTags)
            AvailableGameTags.Add(t);
    }

    private async Task RefreshPlayersAsync()
    {
        if (_clip is null) return;

        var tagged = await _playerService.GetByClipAsync(_clip.Id);
        ClipPlayerChips.Clear();
        foreach (var p in tagged)
        {
            var playerId = p.Id;
            var name     = p.DisplayName;
            ClipPlayerChips.Add(new TagChipViewModel(playerId, name, RemovePlayerAsync));
        }
    }

    private async Task LoadPlayerPickerAsync()
    {
        var all = await _playerService.GetAllAsync();
        AvailablePlayers.Clear();
        foreach (var p in all)
            AvailablePlayers.Add(p);
    }

    private async Task AddPlayerAsync()
    {
        if (_clip is null || SelectedPlayer is null) return;

        await _playerService.TagClipAsync(_clip.Id, SelectedPlayer.Id);
        SelectedPlayer = null;
        await RefreshPlayersAsync();
    }

    private async Task RemovePlayerAsync(TagChipViewModel chip)
    {
        if (_clip is null) return;
        await _playerService.UntagClipAsync(_clip.Id, chip.TagId);
        ClipPlayerChips.Remove(chip);
    }

    private void BeginAddHighlight()
    {
        // Mutual exclusion: close the trim form so both handle-sets never appear simultaneously.
        Trim.IsTrimming = false;

        IsAddingHighlight = true;
    }

    private void MarkHighlightStart()
    {
        // Use Playback.PositionSeconds (what the slider shows) rather than MediaPlayer.Time (VLC internal
        // clock which can lag after a seek), so the triangle lands exactly on the slider thumb.
        var t = TimeSpan.FromSeconds(Playback.PositionSeconds);
        if (_editingHighlight is not null)
        {
            _editingHighlight.EditStartDisplay = PlaybackViewModel.FormatPrecise(t);
            _editHighlightStart                = t;
        }
        else
        {
            _highlightStart       = t;
            HighlightStartDisplay = PlaybackViewModel.FormatPrecise(t);
        }
        OnPropertyChanged(nameof(HighlightStartFraction));
    }

    private void MarkHighlightEnd()
    {
        var t = TimeSpan.FromSeconds(Playback.PositionSeconds);
        if (_editingHighlight is not null)
        {
            _editingHighlight.EditEndDisplay = PlaybackViewModel.FormatPrecise(t);
            _editHighlightEnd                = t;
        }
        else
        {
            _highlightEnd       = t;
            HighlightEndDisplay = PlaybackViewModel.FormatPrecise(t);
        }
        OnPropertyChanged(nameof(HighlightEndFraction));
    }

    /// <summary>
    /// Sets the highlight start time from a proportional canvas position dragged by the user.
    /// Called from the view code-behind drag handler for the start handle.
    /// </summary>
    /// <param name="fraction">Horizontal fraction in [0, 1] relative to the canvas width.</param>
    public void SetHighlightStartFromFraction(double fraction)
    {
        var t = TimeSpan.FromSeconds(Math.Clamp(fraction * Playback.DurationSeconds, 0, Playback.DurationSeconds));
        if (_editingHighlight is not null)
        {
            _editingHighlight.EditStartDisplay = PlaybackViewModel.FormatPrecise(t);
            _editHighlightStart                = t;
        }
        else
        {
            _highlightStart       = t;
            HighlightStartDisplay = PlaybackViewModel.FormatPrecise(t);
        }
        OnPropertyChanged(nameof(HighlightStartFraction));
    }

    /// <summary>
    /// Sets the highlight end time from a proportional canvas position dragged by the user.
    /// Called from the view code-behind drag handler for the end handle.
    /// </summary>
    /// <param name="fraction">Horizontal fraction in [0, 1] relative to the canvas width.</param>
    public void SetHighlightEndFromFraction(double fraction)
    {
        var t = TimeSpan.FromSeconds(Math.Clamp(fraction * Playback.DurationSeconds, 0, Playback.DurationSeconds));
        if (_editingHighlight is not null)
        {
            _editingHighlight.EditEndDisplay = PlaybackViewModel.FormatPrecise(t);
            _editHighlightEnd                = t;
        }
        else
        {
            _highlightEnd       = t;
            HighlightEndDisplay = PlaybackViewModel.FormatPrecise(t);
        }
        OnPropertyChanged(nameof(HighlightEndFraction));
    }

    /// <summary>
    /// Adds a tag to the pending collection shown in the add-highlight form.
    /// The tag is applied to the newly created highlight inside <see cref="SaveHighlightAsync"/>.
    /// </summary>
    public void AddPendingHighlightTag(Tag tag)
    {
        if (PendingHighlightTags.Any(c => c.TagId == tag.Id)) return;
        PendingHighlightTags.Add(new TagChipViewModel(
            tag.Id, tag.Name,
            chip => { PendingHighlightTags.Remove(chip); return Task.CompletedTask; }));
    }

    private void ResetHighlightForm()
    {
        IsAddingHighlight   = false;
        NewHighlightLabel   = string.Empty;
        _highlightStart     = TimeSpan.Zero;
        _highlightEnd       = TimeSpan.Zero;
        HighlightStartDisplay = "0:00";
        HighlightEndDisplay   = "0:00";
        HighlightAddError   = null;
        PendingHighlightTags.Clear();
        OnPropertyChanged(nameof(HighlightStartFraction));
        OnPropertyChanged(nameof(HighlightEndFraction));
    }

    private async Task SaveHighlightAsync()
    {
        if (_clip is null) return;

        if (_highlightStart >= _highlightEnd)
        {
            HighlightAddError = "End time must be after start time.";
            return;
        }

        HighlightAddError = null;
        var created = await _highlightService.CreateAsync(
            _clip.Id,
            _highlightStart,
            _highlightEnd,
            string.IsNullOrWhiteSpace(NewHighlightLabel) ? null : NewHighlightLabel.Trim());

        foreach (var chip in PendingHighlightTags.ToList())
            await _highlightService.AddTagAsync(created.Id, chip.TagId);

        ResetHighlightForm();
        await RefreshHighlightsAsync();
        _soundService.Play(SoundEffect.HighlightCreated);
        HighlightsChanged?.Invoke();
    }

    private void JumpToHighlight(HighlightViewModel highlight)
    {
        LockedHighlight   = highlight;
        MediaPlayer.Time  = (long)highlight.StartTime.TotalMilliseconds;
    }

    private async Task DeleteHighlightAsync(HighlightViewModel highlight)
    {
        if (LockedHighlight == highlight)
            LockedHighlight = null;

        await _highlightService.DeleteAsync(highlight.HighlightId);
        await RefreshHighlightsAsync();
        // After deleting a highlight, tags it held may no longer be locked on the clip.
        await RefreshClipAndTagsAsync();
        HighlightsChanged?.Invoke();
    }

    private async Task SaveNotesAsync()
    {
        if (_clip is null) return;
        await _clipService.SetNotesAsync(_clip.Id, Notes);
    }

    private async Task MarkAsReviewedAsync()
    {
        if (_clip is null) return;
        await _clipService.SetStatusAsync(_clip.Id, ClipStatus.Reviewed);
        IsUnreviewed = false;
        ClipStatusChanged?.Invoke();
    }

    private async Task ToggleFavouriteAsync()
    {
        if (_clip is null) return;
        await _clipService.ToggleFavouriteAsync(_clip.Id);
        IsFavourite = !IsFavourite;
    }

    private async Task SetRatingAsync(int rating)
    {
        if (_clip is null) return;
        await _clipService.SetRatingAsync(_clip.Id, rating);
        Rating = rating;
    }

    /// <summary>Sets the star rating of the currently watched highlight.</summary>
    private async Task SetWatchRatingAsync(int rating)
    {
        if (WatchHighlightId is null) return;
        await _highlightService.SetRatingAsync(WatchHighlightId.Value, rating);
        WatchHighlightRating = rating;
    }

    /// <summary>Toggles the favourite state of the currently watched highlight.</summary>
    private async Task ToggleWatchFavoriteAsync()
    {
        if (WatchHighlightId is null) return;
        await _highlightService.ToggleFavoriteAsync(WatchHighlightId.Value);
        WatchHighlightIsFavorite = !WatchHighlightIsFavorite;
    }

    // ---- Tag operations ----

    /// <summary>
    /// Adds the given general <paramref name="tag"/> to the clip.
    /// Called from the view's code-behind when the user commits a selection in the AutoCompleteBox
    /// (Enter key or mouse click), bypassing the reactive-property path to avoid premature
    /// execution during keyboard navigation.
    /// </summary>
    public async Task AddGeneralTagDirectlyAsync(Tag tag)
    {
        if (_clip is null) return;

        await _clipService.AddTagAsync(_clip.Id, tag.Id);
        _clip = await _clipService.GetByIdAsync(_clip.Id);
        RefreshTags();
        await AutoMarkReviewedIfEnabledAsync();
    }

    /// <summary>
    /// Confirms the given game <paramref name="tag"/> on the clip.
    /// Called from the view's code-behind when the user commits a selection in the AutoCompleteBox.
    /// </summary>
    public async Task AddGameTagDirectlyAsync(Tag tag)
    {
        if (_clip is null) return;

        // If the clip was imported with a suggested game name and the user wants to remember this
        // association, persist it as a game-tag alias for automatic assignment on future imports.
        if (RememberGameAlias && !string.IsNullOrWhiteSpace(_clip.SuggestedGameName))
            await _gameTagAliasService.EnsureAliasAsync(_clip.SuggestedGameName, tag.Id);

        await _clipService.ConfirmGameTagAsync(_clip.Id, tag.Id);
        _clip = await _clipService.GetByIdAsync(_clip.Id);
        SuggestedGameNameDisplay = null;
        RefreshTags();
        await AutoMarkReviewedIfEnabledAsync();
    }

    // Keep the backing AsyncRelayCommand wrappers so the existing command declarations still compile.
    private Task AddGeneralTagAsync() => Task.CompletedTask;
    private Task AddGameTagAsync()    => Task.CompletedTask;

    private async Task AutoMarkReviewedIfEnabledAsync()
    {
        if (_clip is null) return;
        if (!_settingsService.Current.AutoMarkReviewedOnTagAdd) return;
        if (_clip.Status != ClipStatus.Unreviewed) return;

        await _clipService.SetStatusAsync(_clip.Id, ClipStatus.Reviewed);
        _clip.Status = ClipStatus.Reviewed;
        IsUnreviewed = false;
        ClipStatusChanged?.Invoke();
    }

    private async Task RemoveClipTagAsync(TagChipViewModel chip)
    {
        if (_clip is null) return;
        await _clipService.RemoveTagAsync(_clip.Id, chip.TagId);
        ClipTags.Remove(chip);
    }

    private async Task RemoveClipGameTagAsync(TagChipViewModel chip)
    {
        if (_clip is null) return;
        await _clipService.RemoveTagAsync(_clip.Id, chip.TagId);
        ClipGameTag = null;
    }

    private async Task ClearAllTagsAsync()
    {
        if (_clip is null) return;

        // Only remove tags that are directly applied to the clip (not locked by a highlight).
        foreach (var chip in ClipTags.Where(c => !c.IsPropagated).ToList())
            await _clipService.RemoveTagAsync(_clip.Id, chip.TagId);

        IsClearAllTagsConfirmVisible = false;
        await RefreshClipAndTagsAsync();
    }

    private async Task ClearAllPlayersAsync()
    {
        if (_clip is null) return;

        foreach (var chip in ClipPlayerChips.ToList())
            await _playerService.UntagClipAsync(_clip.Id, chip.TagId);

        IsClearAllPlayersConfirmVisible = false;
        ClipPlayerChips.Clear();
    }

    /// <summary>
    /// Removes all directly-applied tags and players from the clip, resets the rating and favourite
    /// flag to their defaults, and sets the clip status back to <see cref="ClipStatus.Unreviewed"/>.
    /// </summary>
    private async Task ClearAllDataAsync()
    {
        if (_clip is null) return;

        // Remove all directly-applied general and game tags.
        foreach (var chip in ClipTags.Where(c => !c.IsPropagated).ToList())
            await _clipService.RemoveTagAsync(_clip.Id, chip.TagId);
        if (ClipGameTag is not null && !ClipGameTag.IsPropagated)
            await _clipService.RemoveTagAsync(_clip.Id, ClipGameTag.TagId);

        // Remove all player associations.
        foreach (var chip in ClipPlayerChips.ToList())
            await _playerService.UntagClipAsync(_clip.Id, chip.TagId);

        // Reset rating if set.
        if (Rating != 0)
            await _clipService.SetRatingAsync(_clip.Id, 0);

        // Clear favourite if set.
        if (IsFavourite)
            await _clipService.ToggleFavouriteAsync(_clip.Id);

        // Reset status to Unreviewed.
        await _clipService.SetStatusAsync(_clip.Id, ClipStatus.Unreviewed);

        IsClearAllDataConfirmVisible = false;

        // Reload the full clip state.
        await LoadAsync(_clip.Id);
    }

    private async Task AddTagToHighlightAsync(HighlightViewModel highlight, int tagId)
    {
        await _highlightService.AddTagAsync(highlight.HighlightId, tagId);

        // Propagate the same tag to the parent clip (idempotent — no-op if already tagged).
        if (_clip is not null)
        {
            await _clipService.AddTagAsync(_clip.Id, tagId);
            _clip = await _clipService.GetByIdAsync(_clip.Id);
            RefreshTags();
        }

        await RefreshHighlightsAsync();
    }

    private async Task RemoveTagFromHighlightAsync(HighlightViewModel highlight, int tagId)
    {
        await _highlightService.RemoveTagAsync(highlight.HighlightId, tagId);
        await RefreshHighlightsAsync();
        // After removing the tag from a highlight, unlock the clip-level chip if no
        // other highlight still holds the same tag.
        await RefreshClipAndTagsAsync();
    }

    // ---- Rename ----

    private async Task ConfirmRenameAsync()
    {
        if (_clip is null || string.IsNullOrWhiteSpace(RenameValue)) return;

        var newName    = RenameValue.Trim();
        var destructive = _settingsService.Current.DefaultTrimMode == TrimMode.Destructive;
        await _clipService.RenameAsync(_clip.Id, newName, destructive);
        ClipTitle  = newName;
        IsRenaming = false;
        ClipRenamed?.Invoke(_clip.Id, newName);
    }

    /// <summary>
    /// Processes all pending export jobs in the background immediately after one is queued.
    /// Guards against concurrent runs so multiple rapid queues do not race.
    /// </summary>
    private async Task RunExportQueueAsync()
    {
        if (_isProcessingExport) return;
        _isProcessingExport    = true;
        IsExporting            = true;
        ExportStatusMessage    = "Exporting...";

        try
        {
            await _exportService.ProcessQueueAsync();
            ExportStatusMessage = "Export complete.";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting         = false;
            _isProcessingExport = false;
        }
    }

    // ---- Delete / trash ----

    private async Task ConfirmDeleteAsync()
    {
        if (_clip is null) return;

        PrepareForClose();

        // Give VLC a moment to release the file handle before attempting to move it.
        await Task.Delay(400);

        try
        {
            await _clipService.TrashAsync(_clip.Id);
        }
        catch (Exception ex)
        {
            // Restore the confirmation strip so the user can retry or cancel.
            IsDeleteConfirmVisible = true;
            System.Diagnostics.Debug.WriteLine($"[ClipStudio] TrashAsync failed: {ex.Message}");
            return;
        }

        _soundService.Play(SoundEffect.ClipTrashed);
        ClipStatusChanged?.Invoke();
        BackRequested?.Invoke();
    }

    // ---- Open in file explorer ----

    private void OpenInExplorer()
    {
        if (_clip is null) return;

        var path = _clip.FilePath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start(new ProcessStartInfo("open", $"-R \"{path}\"") { UseShellExecute = true });
        }
        else
        {
            var dir = Path.GetDirectoryName(path) ?? path;
            Process.Start(new ProcessStartInfo("xdg-open", $"\"{dir}\"") { UseShellExecute = true });
        }
    }

    // ---- Locked-highlight change ----

    /// <summary>
    /// Called by the source generator when <see cref="LockedHighlight"/> changes.
    /// Synchronises the <see cref="HighlightViewModel.IsLocked"/> flag on each highlight row.
    /// </summary>
    partial void OnLockedHighlightChanged(HighlightViewModel? value)
    {
        foreach (var h in Highlights)
            h.IsLocked = h == value;
    }

    // ---- LibVLC event handlers (raised on a background thread) ----

    private void OnPlayerTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        // Drops mid-drag updates and the stale pre-seek events VLC fires after a seek.
        if (Playback.ShouldIgnoreTimeChanged(e.Time)) return;

        Dispatcher.UIThread.Post(() =>
        {
            var ts = TimeSpan.FromMilliseconds(e.Time);

            Transcription.UpdatePlaybackPosition(e.Time);

            if (IsWatchMode)
            {
                // When the watched highlight's end is reached, behaviour depends on LoopMode.
                if (ts >= WatchEnd)
                {
                    switch (Playback.LoopMode)
                    {
                        case LoopMode.LoopThis:
                            // Loop back to the start of this highlight.
                            Playback.SeekToMs((long)WatchStart.TotalMilliseconds);
                            break;

                        case LoopMode.LoopAll:
                            // Pause immediately so VLC does not continue playing past the
                            // highlight end while the next highlight is being loaded.
                            MediaPlayer.Pause();
                            // Always invoke NextHighlightRequested; the MainWindowViewModel
                            // lambda handles wrap-around from the last highlight to the first.
                            NextHighlightRequested?.Invoke();
                            break;

                        case LoopMode.Off:
                        default:
                            // Pause and seek back to highlight start so that pressing play
                            // restarts the current highlight rather than continuing into the clip.
                            MediaPlayer.Pause();
                            Playback.SeekToMs((long)WatchStart.TotalMilliseconds);
                            Playback.UpdatePositionDisplay(TimeSpan.Zero);
                            break;
                    }
                    return;
                }

                // The child renders the position relative to the window start.
                Playback.UpdatePositionFromPlayer(ts);
                return;
            }

            Playback.UpdatePositionFromPlayer(ts);

            // Highlight loop: if a highlight is locked and the position has passed its end,
            // seek back only when looping is enabled.
            if (LockedHighlight is not null && ts >= LockedHighlight.EndTime
                && Playback.LoopMode != LoopMode.Off)
                MediaPlayer.Time = (long)LockedHighlight.StartTime.TotalMilliseconds;
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Debug scaffolding, kept while the audio issues are open: it appends to
    /// <c>%TEMP%\clipstudio_audio.log</c> on every play event. Remove once they are resolved.
    /// </remarks>
    public void LogAudioDiagnostics(string context)
    {
        try
        {
            var logPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "clipstudio_audio.log");
            var tracks = MediaPlayer.AudioTrackDescription;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{System.DateTime.Now:HH:mm:ss.fff}] {context}");
            sb.AppendLine($"  Volume={MediaPlayer.Volume}  Mute={MediaPlayer.Mute}  AudioTrack={MediaPlayer.AudioTrack}");
            sb.AppendLine($"  AudioTrackDescription ({tracks.Length} entries):");
            foreach (var t in tracks)
                sb.AppendLine($"    Id={t.Id}  Name={t.Name}");
            sb.AppendLine($"  Media={MediaPlayer.Media?.Mrl ?? "(null)"}");
            System.IO.File.AppendAllText(logPath, sb.ToString());
        }
        catch { }
    }

    private void OnPlayerPlaying(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            Playback.IsPlaying = true;
            LogAudioDiagnostics("OnPlayerPlaying");

            // After a media reload (remux preview or clean-reload for mode transition), seek back
            // to the position saved before the reload. The track list is already populated so
            // the refresh below is skipped by the HasTracks guard.
            if (_mixReloadSeekMs >= 0)
            {
                var seekTarget   = _mixReloadSeekMs;
                _mixReloadSeekMs = -1;
                Playback.ArmSeekGuard(seekTarget);

                // Apply a deferred native track selection when dropping a remux preview and
                // returning to the original file with a specific track selected.
                if (_pendingNativeTrackId != -2)
                {
                    var tid               = _pendingNativeTrackId;
                    _pendingNativeTrackId = -2;
                    MediaPlayer.SetAudioTrack(tid);
                }

                MediaPlayer.Time = seekTarget;
                return;
            }

            // Only populate audio tracks on first play of this clip.
            if (!Audio.HasTracks)
                _ = Audio.RefreshAudioTracksAsync();

            // After end-of-clip with loop off: immediately pause at position 0 so the user
            // can replay by pressing play or scrubbing without needing to reload the clip.
            if (_replayAfterEnd)
            {
                _replayAfterEnd = false;
                MediaPlayer.Pause();
                Playback.UpdatePositionDisplay(TimeSpan.Zero);
                return;
            }

            // In watch mode with LoopOff: the clip reached its natural end at WatchEnd.
            // Seek back to WatchStart then pause so the user can replay the highlight.
            if (_watchModeEndPending)
            {
                _watchModeEndPending = false;
                Playback.SeekToMs((long)WatchStart.TotalMilliseconds);
                MediaPlayer.Pause();
                Playback.UpdatePositionDisplay(TimeSpan.Zero);
                return;
            }

            // In watch mode: seek to the highlight start as soon as playback starts.
            if (_watchModeSeekPending)
            {
                _watchModeSeekPending = false;
                Playback.SeekToMs((long)WatchStart.TotalMilliseconds);
            }
        });

    private void OnPlayerPaused(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => Playback.IsPlaying = false);

    private void OnPlayerStopped(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => Playback.IsPlaying = false);

    private void OnPlayerEndReached(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            Playback.IsPlaying = false;

            if (IsWatchMode)
            {
                switch (Playback.LoopMode)
                {
                    case LoopMode.Off:
                        // Clip reached its natural end exactly at WatchEnd. Restart media so we
                        // can seek back to WatchStart and then pause, ready for the user to replay.
                        _watchModeEndPending = true;
                        MediaPlayer.Stop();
                        MediaPlayer.Play();
                        return;

                    case LoopMode.LoopAll:
                        // Always invoke NextHighlightRequested; the lambda handles wrap-around
                        // from the last highlight back to the first.
                        NextHighlightRequested?.Invoke();
                        return;

                    default: // LoopThis
                        MediaPlayer.Stop();
                        _watchModeSeekPending = true;
                        MediaPlayer.Play();
                        return;
                }
            }

            switch (Playback.LoopMode)
            {
                case LoopMode.LoopThis:
                    MediaPlayer.Stop();
                    MediaPlayer.Play();
                    break;

                case LoopMode.LoopAll:
                    if (HasNext)
                        NextClipRequested?.Invoke();
                    break;

                default: // Off
                    // Restart then immediately pause at position 0 so the user can replay
                    // by pressing play or scrubbing without needing to reload the clip.
                    _replayAfterEnd = true;
                    MediaPlayer.Stop();
                    MediaPlayer.Play();
                    break;
            }
        });

    // ---- Transcription helpers ----

    /// <summary>
    /// Seeks the media player to the given absolute position in milliseconds.
    /// Used as the seek callback passed to <see cref="TranscriptionViewModel"/> so that
    /// clicking a segment row in the transcription panel moves the playhead.
    /// </summary>
    /// <param name="ms">Target position in milliseconds from the start of the clip.</param>
    private void SeekToMs(long ms) => Playback.SeekToMs(ms);

    /// <summary>
    /// Called when <see cref="TranscriptionViewModel"/> raises <c>TranscriptionCompleted</c>
    /// after a successful transcription run. Updates <see cref="HasTranscription"/> and caches
    /// the SRT path so that <see cref="ToggleSubtitlesCommand"/> can apply the subtitle slave.
    /// </summary>
    /// <param name="srtPath">Absolute path to the generated <c>.srt</c> file.</param>
    private void OnTranscriptionCompleted(string srtPath)
    {
        _latestSrtPath  = srtPath;
        HasTranscription = true;
    }

    /// <summary>
    /// Called when a segment's text has been edited and the SRT file on disk has been rewritten.
    /// If the subtitle overlay is currently active, reloads the media with the updated slave so
    /// LibVLC picks up the changed file content.
    /// </summary>
    private void OnSegmentTextEdited()
    {
        if (!IsSubtitlesEnabled || _clip is null || string.IsNullOrEmpty(_latestSrtPath))
            return;

        var posMs = MediaPlayer.Time;
        var uri   = new Uri(_latestSrtPath).AbsoluteUri;

        _media?.Dispose();
        _media = new Media(_libVlc, _clip.FilePath, FromType.FromPath);
        MediaPlayer.Media = _media;
        Playback.ArmSeekGuard(posMs);
        MediaPlayer.Play();
        MediaPlayer.AddSlave(MediaSlaveType.Subtitle, uri, true);
        MediaPlayer.Time = posMs;
    }

    /// <summary>
    /// Toggles the LibVLC subtitle slave on or off. When enabling, attaches the latest
    /// <c>.srt</c> file as a subtitle slave; when disabling, reloads the media without any slave.
    /// </summary>
    private void ToggleSubtitles()
    {
        if (!HasTranscription || string.IsNullOrEmpty(_latestSrtPath))
            return;

        if (!IsSubtitlesEnabled)
        {
            // Enable: attach the SRT file as a subtitle slave.
            var uri = new Uri(_latestSrtPath).AbsoluteUri;
            MediaPlayer.AddSlave(MediaSlaveType.Subtitle, uri, true);
            IsSubtitlesEnabled = true;
        }
        else
        {
            // Disable: reload the current media without any slave attached.
            IsSubtitlesEnabled = false;
            if (_clip is not null)
            {
                var posMs = MediaPlayer.Time;
                _media?.Dispose();
                _media = new Media(_libVlc, _clip.FilePath, FromType.FromPath);
                MediaPlayer.Media = _media;
                Playback.ArmSeekGuard(posMs);
                MediaPlayer.Play();
                MediaPlayer.Time = posMs;
            }
        }
    }

    // ---- IDisposable ----

    /// <inheritdoc/>
    public void Dispose()
    {
        Audio.Dispose();

        Transcription.SegmentTextEdited -= OnSegmentTextEdited;

        MediaPlayer.TimeChanged -= OnPlayerTimeChanged;
        MediaPlayer.Playing     -= OnPlayerPlaying;
        MediaPlayer.Paused      -= OnPlayerPaused;
        MediaPlayer.Stopped     -= OnPlayerStopped;
        MediaPlayer.EndReached  -= OnPlayerEndReached;

        _media?.Dispose();
        _media = null;

        MediaPlayer.Dispose();
    }
}
