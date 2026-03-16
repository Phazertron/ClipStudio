using System;
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
public sealed partial class ClipDetailViewModel : ViewModelBase, IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly IClipService _clipService;
    private readonly IHighlightService _highlightService;
    private readonly IScreenshotService _screenshotService;
    private readonly ITagService _tagService;
    private readonly IExportService _exportService;
    private readonly ISettingsService _settingsService;
    private readonly IPlayerService _playerService;
    private readonly IAudioTrackService _audioTrackService;
    private readonly ClipStudio.Application.Interfaces.IMixedAudioService _mixedAudioService;
    private readonly ClipStudio.Application.Interfaces.IGameTagAliasService _gameTagAliasService;
    private readonly ClipStudio.UI.Services.ISoundService _soundService;
    private readonly ClipStudio.Application.Interfaces.ITagSuggestionService _tagSuggestionService;

    private Clip? _clip;
    private Media? _media;
    private bool _isUpdatingFromPlayer;
    private bool _isDragging;

    /// <summary>
    /// Cancellation token source for the debounced audio-routing update task.
    /// Cancelled and replaced whenever audio track settings change.
    /// </summary>
    private System.Threading.CancellationTokenSource? _mixDebounce;

    /// <summary>
    /// Cancellation token source for an in-progress FFmpeg remux generation.
    /// Cancelled when a new mix is requested before the previous one finishes, or when
    /// the clip is closed. Prevents concurrent writes to the same output file.
    /// </summary>
    private System.Threading.CancellationTokenSource? _mixApplyCts;

    /// <summary>
    /// Index (0 or 1) of the cache slot that VLC is currently playing.
    /// The next generation always targets the <em>other</em> slot so that FFmpeg never tries
    /// to overwrite a file that VLC holds open on Windows.
    /// Slot 0 → <c>clip_{id}_audio_preview.mkv</c>; slot 1 → <c>clip_{id}_audio_preview_alt.mkv</c>.
    /// </summary>
    private int _activeMixSlot;

    /// <summary>
    /// When >= 0, the next <see cref="OnPlayerPlaying"/> callback should seek back to this
    /// position (ms) after a VLC media reload triggered by a mode transition.
    /// </summary>
    private long _mixReloadSeekMs = -1;

    /// <summary>
    /// Tracks the current audio rendering mode so that mode transitions can be detected and
    /// the appropriate action (native track switch vs. media reload) can be taken.
    /// </summary>
    private AudioMode _audioMode = AudioMode.NativeSingleTrack;

    /// <summary>
    /// File path of the last successfully generated FFmpeg remux preview (MKV).
    /// Null when no preview has been generated for the current clip/settings.
    /// </summary>
    private string? _mixedPreviewPath;

    /// <summary>
    /// VLC track ID to apply via <c>SetAudioTrack</c> immediately after the next media reload
    /// completes. -2 means no pending native track selection; -1 means mute (disable audio);
    /// any non-negative value is the track ID to select.
    /// </summary>
    private int _pendingNativeTrackId = -2;

    /// <summary>
    /// Gets or sets a value indicating whether an FFmpeg remux operation is in progress.
    /// Used to show a spinner on the Apply Mix button while the mix is being generated.
    /// </summary>
    [ObservableProperty] private bool _isMixApplying;

    /// <summary>
    /// Gets or sets a value indicating whether audio settings have changed and the user needs
    /// to click Apply Mix to hear the updated mix. Only relevant in <see cref="AudioMode.MixedRemux"/> mode.
    /// </summary>
    [ObservableProperty] private bool _hasPendingAudioChanges;

    /// <summary>
    /// Gets or sets a value indicating whether VLC is currently playing the FFmpeg remux preview
    /// file rather than the original clip. Shown in the UI as a "Playing preview" badge.
    /// </summary>
    [ObservableProperty] private bool _isPlayingMixPreview;

    /// <summary>
    /// After a programmatic seek, time-changed events reporting a position below this threshold
    /// are discarded; they are stale pre-seek events fired by VLC before it finishes seeking.
    /// Reset to -1 once a valid (post-seek) event arrives.
    /// </summary>
    private long _ignoreTimeChangedBeforeMs = -1;

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

    // ---- Player state ----

    /// <summary>
    /// Gets the LibVLC <see cref="LibVLCSharp.Shared.MediaPlayer"/> instance.
    /// Bound to the <c>VideoView</c> in the XAML.
    /// </summary>
    public MediaPlayer MediaPlayer { get; }

    /// <summary>Gets or sets the filename / title shown in the detail header.</summary>
    [ObservableProperty] private string _clipTitle = string.Empty;

    /// <summary>Gets or sets a value indicating whether the player is currently playing.</summary>
    [ObservableProperty] private bool _isPlaying;

    /// <summary>
    /// Gets or sets the current playback position in seconds.
    /// Bound one-way (from VM to slider); seek on user interaction is handled via <see cref="EndScrub"/>.
    /// </summary>
    [ObservableProperty] private double _positionSeconds;

    /// <summary>Gets or sets the total clip duration in seconds. Used as the slider maximum.</summary>
    [ObservableProperty] private double _durationSeconds;

    /// <summary>Gets or sets the formatted playback position string, e.g. <c>1:23</c>.</summary>
    [ObservableProperty] private string _positionDisplay = "0:00";

    /// <summary>Gets or sets the formatted total duration string.</summary>
    [ObservableProperty] private string _durationDisplay = "0:00";

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

    private TimeSpan _highlightStart = TimeSpan.Zero;
    private TimeSpan _highlightEnd   = TimeSpan.Zero;

    /// <summary>
    /// Gets the proportional position (0.0-1.0) of the highlight start mark within the clip.
    /// Used to position the start handle on the timeline overlay.
    /// </summary>
    public double HighlightStartFraction =>
        DurationSeconds > 0 ? _highlightStart.TotalSeconds / DurationSeconds : 0;

    /// <summary>
    /// Gets the proportional position (0.0-1.0) of the highlight end mark within the clip.
    /// Used to position the end handle on the timeline overlay.
    /// </summary>
    public double HighlightEndFraction =>
        DurationSeconds > 0 ? _highlightEnd.TotalSeconds / DurationSeconds : 0;

    // ---- Trim & Export ----

    /// <summary>
    /// Gets the proportional position (0.0-1.0) of the trim start mark within the clip.
    /// Used to position the trim-start handle on the timeline overlay.
    /// </summary>
    public double TrimStartFraction =>
        DurationSeconds > 0 ? _trimStart.TotalSeconds / DurationSeconds : 0;

    /// <summary>
    /// Gets the proportional position (0.0-1.0) of the trim end mark within the clip.
    /// Used to position the trim-end handle on the timeline overlay.
    /// </summary>
    public double TrimEndFraction =>
        DurationSeconds > 0 ? _trimEnd.TotalSeconds / DurationSeconds : 0;

    /// <summary>Gets or sets a value indicating whether the Trim and Export form is expanded.</summary>
    [ObservableProperty] private bool _isTrimming;

    /// <summary>Gets or sets the formatted display of the trim start time.</summary>
    [ObservableProperty] private string _trimStartDisplay = "0:00";

    /// <summary>Gets or sets the formatted display of the trim end time.</summary>
    [ObservableProperty] private string _trimEndDisplay = "0:00";

    /// <summary>Gets or sets the validation error for the trim start input. Null when valid.</summary>
    [ObservableProperty] private string? _trimStartError;

    /// <summary>Gets or sets the validation error for the trim end input. Null when valid.</summary>
    [ObservableProperty] private string? _trimEndError;

    /// <summary>Gets or sets the output file path for the trimmed export.</summary>
    [ObservableProperty] private string _trimOutputPath = string.Empty;

    /// <summary>
    /// Gets or sets whether the trim export for this clip should use destructive mode
    /// (re-encode and optionally delete original). Defaults to the setting's <see cref="AppSettings.DefaultTrimMode"/>.
    /// Shown highlighted in red when true to warn the user of the irreversible action.
    /// </summary>
    [ObservableProperty] private bool _isTrimDestructive;

    /// <summary>
    /// Gets or sets a warning message shown when a destructive trim would clip highlights that fall
    /// outside the selected trim range. Non-null triggers a confirmation UI. Cleared on confirm or cancel.
    /// </summary>
    [ObservableProperty] private string? _trimDestructiveWarning;

    private TimeSpan _trimStart = TimeSpan.Zero;
    private TimeSpan _trimEnd   = TimeSpan.Zero;

    // ---- Highlight loop ----

    /// <summary>
    /// Gets or sets the highlight whose time range currently constrains playback (loop mode).
    /// When set, the player seeks back to <see cref="HighlightViewModel.StartTime"/> whenever
    /// the position reaches <see cref="HighlightViewModel.EndTime"/>.
    /// </summary>
    [ObservableProperty] private HighlightViewModel? _lockedHighlight;

    // ---- Loop mode and queue navigation ----

    /// <summary>Gets or sets the loop behaviour when a clip or highlight reaches its end.</summary>
    [ObservableProperty] private LoopMode _loopMode = LoopMode.Off;

    /// <summary>Gets a value indicating whether loop mode is <see cref="LoopMode.Off"/>.</summary>
    public bool IsLoopOff   => LoopMode == LoopMode.Off;

    /// <summary>Gets a value indicating whether loop mode is <see cref="LoopMode.LoopThis"/>.</summary>
    public bool IsLoopThis  => LoopMode == LoopMode.LoopThis;

    /// <summary>Gets a value indicating whether loop mode is <see cref="LoopMode.LoopAll"/>.</summary>
    public bool IsLoopAll   => LoopMode == LoopMode.LoopAll;

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

    // ---- Audio tracks ----

    /// <summary>Gets the audio tracks detected in the current clip.</summary>
    public ObservableCollection<AudioTrackViewModel> AudioTracks { get; } = new();

    /// <summary>
    /// Gets or sets the master volume level (0–200, 100 = 100 %).
    /// Changes are applied to the VLC media player immediately unless <see cref="IsMuted"/> is true.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIconKind))]
    private int _masterVolume = 100;

    /// <summary>
    /// Gets or sets whether the master audio output is muted.
    /// When <see langword="true"/>, VLC volume is set to zero regardless of <see cref="MasterVolume"/>.
    /// The slider value is preserved and restored when unmuting.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeIconKind))]
    private bool _isMuted;

    /// <summary>
    /// Gets the Material icon kind that reflects the current master volume state.
    /// Returns <see cref="MaterialIconKind.VolumeMute"/> when muted or volume is zero,
    /// and scales between Low/Medium/High otherwise.
    /// </summary>
    public MaterialIconKind VolumeIconKind =>
        IsMuted || MasterVolume == 0 ? MaterialIconKind.VolumeMute  :
        MasterVolume <= 50           ? MaterialIconKind.VolumeLow   :
        MasterVolume <= 100          ? MaterialIconKind.VolumeMedium :
                                         MaterialIconKind.VolumeHigh;

    // ---- Collections ----

    /// <summary>Gets the highlights associated with the current clip.</summary>
    public ObservableCollection<HighlightViewModel> Highlights { get; } = new();

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

    /// <summary>Gets the command that toggles playback between play and pause.</summary>
    public IRelayCommand PlayPauseCommand { get; }

    /// <summary>Gets the command that seeks the player 10 seconds backward.</summary>
    public IRelayCommand SkipBackCommand { get; }

    /// <summary>Gets the command that seeks the player 10 seconds forward.</summary>
    public IRelayCommand SkipForwardCommand { get; }

    /// <summary>Gets the command that steps the player back by one frame.</summary>
    public IRelayCommand FrameBackCommand { get; }

    /// <summary>Gets the command that steps the player forward by one frame.</summary>
    public IRelayCommand FrameForwardCommand { get; }

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

    /// <summary>Gets the command that captures a screenshot at the current playback position.</summary>
    public IAsyncRelayCommand TakeScreenshotCommand { get; }

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

    /// <summary>Gets the command that expands the Trim and Export form.</summary>
    public IRelayCommand BeginTrimCommand { get; }

    /// <summary>Gets the command that collapses the Trim and Export form without queuing.</summary>
    public IRelayCommand CancelTrimCommand { get; }

    /// <summary>Gets the command that marks the current position as the trim start point.</summary>
    public IRelayCommand MarkTrimStartCommand { get; }

    /// <summary>Gets the command that marks the current position as the trim end point.</summary>
    public IRelayCommand MarkTrimEndCommand { get; }

    /// <summary>Gets the command that commits the user-edited trim start text to the underlying time value.</summary>
    public IRelayCommand CommitTrimStartCommand { get; }

    /// <summary>Gets the command that commits the user-edited trim end text to the underlying time value.</summary>
    public IRelayCommand CommitTrimEndCommand { get; }

    /// <summary>Gets the command that queues a trim export job for the current range.</summary>
    public IAsyncRelayCommand QueueTrimExportCommand { get; }

    /// <summary>
    /// Gets the command that confirms a destructive trim after the user acknowledges the warning
    /// that highlights fall outside the trim range. Bypasses the warning check and queues immediately.
    /// </summary>
    public IAsyncRelayCommand ConfirmDestructiveTrimCommand { get; }

    /// <summary>Gets the command that releases the active highlight loop lock.</summary>
    public IRelayCommand UnlockHighlightCommand { get; }

    /// <summary>Gets the command that toggles the repeat flag.</summary>
    public IRelayCommand ToggleRepeatCommand { get; }

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

    /// <summary>Gets the command that toggles the master audio mute state.</summary>
    public IRelayCommand ToggleMuteCommand { get; }

    /// <summary>Gets the command that resets the master volume to 100 %.</summary>
    public IRelayCommand SetMasterVolumeToFullCommand { get; }

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

    /// <summary>Gets the command that persists the current audio track settings to the database.</summary>
    public IAsyncRelayCommand SaveAudioSettingsCommand { get; }

    /// <summary>
    /// Gets the command that generates the FFmpeg remux preview and reloads VLC with it.
    /// Only relevant in <see cref="AudioMode.MixedRemux"/> mode.
    /// Enabled only when <see cref="HasPendingAudioChanges"/> is true.
    /// </summary>
    public IAsyncRelayCommand ApplyAudioMixCommand { get; }

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
        ClipStudio.Application.Interfaces.IGameTagAliasService gameTagAliasService,
        ClipStudio.UI.Services.ISoundService soundService,
        ClipStudio.Application.Interfaces.ITagSuggestionService tagSuggestionService)
    {
        _libVlc               = libVlc;
        _clipService          = clipService;
        _highlightService     = highlightService;
        _screenshotService    = screenshotService;
        _tagService           = tagService;
        _exportService        = exportService;
        _settingsService      = settingsService;
        _playerService        = playerService;
        _audioTrackService    = audioTrackService;
        _mixedAudioService    = mixedAudioService;
        _gameTagAliasService  = gameTagAliasService;
        _soundService         = soundService;
        _tagSuggestionService = tagSuggestionService;

        MediaPlayer = new MediaPlayer(_libVlc);
        // _masterVolume field initialiser bypasses the generated setter, so OnMasterVolumeChanged
        // is never called during construction and MediaPlayer.Volume is left at whatever LibVLC
        // reads from the system VLC config (vlcrc) — which can be 0. Apply it explicitly here.
        MediaPlayer.Volume      = _masterVolume;
        MediaPlayer.TimeChanged += OnPlayerTimeChanged;
        MediaPlayer.Playing     += OnPlayerPlaying;
        MediaPlayer.Paused      += OnPlayerPaused;
        MediaPlayer.Stopped     += OnPlayerStopped;
        MediaPlayer.EndReached  += OnPlayerEndReached;

        BackCommand               = new RelayCommand(() => BackRequested?.Invoke());
        PlayPauseCommand          = new RelayCommand(TogglePlayPause);
        SkipBackCommand           = new RelayCommand(() => SeekRelative(TimeSpan.FromSeconds(-10)));
        SkipForwardCommand        = new RelayCommand(() => SeekRelative(TimeSpan.FromSeconds(10)));
        FrameBackCommand          = new RelayCommand(() => SeekRelative(TimeSpan.FromSeconds(-FrameDuration)));
        FrameForwardCommand       = new RelayCommand(() => SeekRelative(TimeSpan.FromSeconds(FrameDuration)));
        BeginAddHighlightCommand  = new RelayCommand(BeginAddHighlight);
        CancelAddHighlightCommand = new RelayCommand(ResetHighlightForm);
        MarkHighlightStartCommand = new RelayCommand(MarkHighlightStart);
        MarkHighlightEndCommand   = new RelayCommand(MarkHighlightEnd);
        SaveHighlightCommand      = new AsyncRelayCommand(SaveHighlightAsync);
        TakeScreenshotCommand     = new AsyncRelayCommand(TakeScreenshotAsync);
        SaveNotesCommand          = new AsyncRelayCommand(SaveNotesAsync);
        MarkAsReviewedCommand     = new AsyncRelayCommand(MarkAsReviewedAsync);
        ToggleFavouriteCommand    = new AsyncRelayCommand(ToggleFavouriteAsync);
        AddGeneralTagCommand      = new AsyncRelayCommand(AddGeneralTagAsync);
        AddGameTagCommand         = new AsyncRelayCommand(AddGameTagAsync);
        BeginRenameCommand        = new RelayCommand(() => { RenameValue = ClipTitle; IsRenaming = true; });
        ConfirmRenameCommand      = new AsyncRelayCommand(ConfirmRenameAsync);
        CancelRenameCommand       = new RelayCommand(() => IsRenaming = false);
        RestoreOriginalNameCommand   = new RelayCommand(() => RenameValue = OriginalFileName);
        BeginTrimCommand          = new RelayCommand(BeginTrim);
        CancelTrimCommand         = new RelayCommand(() => IsTrimming = false);
        MarkTrimStartCommand      = new RelayCommand(MarkTrimStart);
        MarkTrimEndCommand        = new RelayCommand(MarkTrimEnd);
        CommitTrimStartCommand    = new RelayCommand(CommitTrimStart);
        CommitTrimEndCommand      = new RelayCommand(CommitTrimEnd);
        QueueTrimExportCommand          = new AsyncRelayCommand(QueueTrimExportAsync);
        ConfirmDestructiveTrimCommand   = new AsyncRelayCommand(ConfirmDestructiveTrimAsync);
        UnlockHighlightCommand    = new RelayCommand(() => LockedHighlight = null);
        ToggleRepeatCommand       = new RelayCommand(CycleLoopMode);
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
        SaveAudioSettingsCommand           = new AsyncRelayCommand(SaveAudioSettingsAsync);
        ApplyAudioMixCommand               = new AsyncRelayCommand(ApplyAudioMixAsync);
        SetRatingCommand          = new AsyncRelayCommand<string>(s => SetRatingAsync(int.TryParse(s, out var r) ? r : 0));
        AcceptSuggestionCommand   = new AsyncRelayCommand<int>(AcceptSuggestionAsync);
        SetWatchRatingCommand     = new RelayCommand<string>(s => _ = SetWatchRatingAsync(int.TryParse(s, out var r) ? r : 0));
        ToggleWatchFavoriteCommand = new RelayCommand(() => _ = ToggleWatchFavoriteAsync());
        DeleteClipCommand         = new RelayCommand(() => IsDeleteConfirmVisible = true);
        ConfirmDeleteCommand      = new AsyncRelayCommand(ConfirmDeleteAsync);
        CancelDeleteCommand       = new RelayCommand(() => IsDeleteConfirmVisible = false);
        OpenInExplorerCommand     = new RelayCommand(OpenInExplorer);
        ToggleMuteCommand              = new RelayCommand(ToggleMute);
        SetMasterVolumeToFullCommand   = new RelayCommand(() => MasterVolume = 100);
        ShowClearAllDataConfirmCommand   = new RelayCommand(() => IsClearAllDataConfirmVisible   = true);
        ConfirmClearAllDataCommand       = new AsyncRelayCommand(ClearAllDataAsync);
        CancelClearAllDataConfirmCommand = new RelayCommand(() => IsClearAllDataConfirmVisible   = false);
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

        if (IsWatchMode)
        {
            var watchDuration = WatchEnd - WatchStart;
            DurationSeconds = watchDuration.TotalSeconds;
            DurationDisplay = FormatTime(watchDuration);
        }
        else
        {
            DurationSeconds = _clip.Duration.TotalSeconds;
            DurationDisplay = FormatTime(_clip.Duration);
        }

        // Reset audio state so the new clip's tracks are discovered on first play.
        // Without this, AudioTracks retains the previous clip's entries and RefreshAudioTracksAsync
        // is skipped because AudioTracks.Count > 0.
        _mixDebounce?.Cancel();
        _mixApplyCts?.Cancel();
        AudioTracks.Clear();
        _audioMode             = AudioMode.NativeSingleTrack;
        _mixReloadSeekMs       = -1;
        _mixedPreviewPath      = null;
        _pendingNativeTrackId  = -2;
        _activeMixSlot         = 0;
        HasPendingAudioChanges = false;
        IsPlayingMixPreview    = false;
        IsMixApplying          = false;

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
        _mixApplyCts?.Cancel();

        if (MediaPlayer.IsPlaying)
            MediaPlayer.Stop();

        MediaPlayer.Media = null;
        _media?.Dispose();
        _media = null;

        // When caching is disabled the mix files are treated as session-only temp files.
        if (_clip is not null && !_settingsService.Current.CacheAudioPreviews)
        {
            for (var slot = 0; slot <= 1; slot++)
            {
                var mkvPath = GetAudioMixCachePath(slot);
                try { System.IO.File.Delete(mkvPath); } catch { /* non-fatal */ }
            }
        }
    }

    // ---- Scrub support (called from code-behind) ----

    /// <summary>Signals that the user has started dragging the position slider.</summary>
    public void BeginScrub() => _isDragging = true;

    /// <summary>
    /// Signals that the user has released the position slider and seeks to the given time.
    /// </summary>
    /// <param name="seconds">The target playback position in seconds.</param>
    public void EndScrub(double seconds)
    {
        _isDragging = false;
        // In watch mode the scrubber position is relative to WatchStart; translate to absolute.
        var absoluteSeconds = IsWatchMode ? WatchStart.TotalSeconds + seconds : seconds;
        var targetMs = (long)(absoluteSeconds * 1000);
        // Ignore any TimeChanged events below this threshold — they are stale pre-seek events
        // that VLC may fire before it finishes processing the seek request.
        _ignoreTimeChangedBeforeMs = targetMs - 200;
        MediaPlayer.Time = targetMs;
        UpdatePositionDisplay(TimeSpan.FromSeconds(seconds));
    }

    // ---- Private helpers ----

    private double FrameDuration => MediaPlayer.Fps > 0 ? 1.0 / MediaPlayer.Fps : 1.0 / 30.0;

    private async Task RefreshHighlightsAsync()
    {
        if (_clip is null) return;

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
                onSetRating:      async (hvm, r) => await _highlightService.SetRatingAsync(hvm.HighlightId, r),
                onToggleFavorite: async (hvm)    => await _highlightService.ToggleFavoriteAsync(hvm.HighlightId)));
        }

        // Re-apply locked state to the refreshed view models.
        if (lockedId.HasValue)
            LockedHighlight = Highlights.FirstOrDefault(h => h.HighlightId == lockedId.Value);
    }

    private async Task UpdateHighlightLabelAsync(HighlightViewModel hvm, string newLabel, TimeSpan newStart, TimeSpan newEnd)
    {
        await _highlightService.UpdateAsync(hvm.HighlightId, newStart, newEnd, newLabel, null);
        // Rebuild the highlights list from DB so the renamed label is immediately visible.
        await RefreshHighlightsAsync();
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
        await _exportService.QueueAsync(
            _clip.Id,
            highlight.HighlightId,
            outputPath,
            settings.DefaultTrimMode,
            settings.DeleteOriginalAfterDestructiveTrim);
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

    // ---- Audio track methods ----

    /// <summary>
    /// Called by the source generator when <see cref="MasterVolume"/> changes.
    /// Applies the new level to the VLC media player unless the output is muted.
    /// </summary>
    partial void OnMasterVolumeChanged(int value)
    {
        if (!IsMuted)
            MediaPlayer.Volume = value;
    }

    /// <summary>
    /// Called by the source generator when <see cref="IsMuted"/> changes.
    /// Applies or restores the master volume on the VLC media player.
    /// </summary>
    partial void OnIsMutedChanged(bool value) =>
        MediaPlayer.Volume = value ? 0 : MasterVolume;

    /// <summary>Toggles the master audio mute state.</summary>
    private void ToggleMute() => IsMuted = !IsMuted;

    private async Task RefreshAudioTracksAsync()
    {
        if (_clip is null) return;

        // AudioTrackDescription is only populated after playback starts.
        // Id -1 is VLC's "Disabled" sentinel; skip it.
        // The enumeration order determines the 0-based FFmpeg stream index for each track.
        var vlcTracks = MediaPlayer.AudioTrackDescription
            .Where(d => d.Id >= 0)
            .Select((d, i) => (Index: d.Id, FfmpegIndex: i, Name: d.Name ?? $"Track {d.Id}"))
            .ToList();

        if (vlcTracks.Count == 0) return;

        var trackPairs = vlcTracks.Select(t => (t.Index, t.Name));
        var settings   = await _audioTrackService.GetOrInitAsync(_clip.Id, trackPairs);

        AudioTracks.Clear();
        for (var i = 0; i < settings.Count; i++)
        {
            var s    = settings[i];
            var ffIdx = vlcTracks.FirstOrDefault(t => t.Index == s.TrackIndex).FfmpegIndex;
            var vm   = new AudioTrackViewModel(
                s.TrackIndex,
                ffmpegStreamIndex: ffIdx,
                s.DisplayName,
                isIncluded: !s.IsMuted,
                s.Volume,
                onChanged: ScheduleMixRegeneration);
            AudioTracks.Add(vm);
        }

        // Apply saved audio routing. Pass isInitialLoad=true so SetAudioTrack is not called
        // from inside the Playing-event handler — VLC's audio pipeline is not fully ready yet.
        await ApplyAudioRoutingAsync(System.Threading.CancellationToken.None, isInitialLoad: true);
        LogAudioDiagnostics("AfterRefreshAudioTracks");
    }

    private async Task SaveAudioSettingsAsync()
    {
        if (_clip is null) return;

        var settings = AudioTracks.Select(t => new ClipStudio.Core.Entities.AudioTrackSetting
        {
            ClipId      = _clip.Id,
            TrackIndex  = t.TrackIndex,
            DisplayName = t.DisplayName,
            IsMuted     = !t.IsIncluded,
            Volume      = t.Volume,
        });

        await _audioTrackService.SaveAsync(_clip.Id, settings);
    }

    /// <summary>
    /// Debounces audio-routing updates so rapid slider moves or toggle changes do not each
    /// trigger an immediate apply. Fires 300 ms after the last change via
    /// <see cref="ApplyAudioRoutingAsync"/>.
    /// </summary>
    private void ScheduleMixRegeneration()
    {
        _mixDebounce?.Cancel();
        _mixDebounce = new System.Threading.CancellationTokenSource();
        var token = _mixDebounce.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, token);
                await Dispatcher.UIThread.InvokeAsync(() => _ = ApplyAudioRoutingAsync(token));
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    /// <summary>
    /// Determines the appropriate <see cref="AudioMode"/> for the current track configuration
    /// and applies it.
    /// <list type="bullet">
    ///   <item><see cref="AudioMode.NativeSingleTrack"/> — calls
    ///   <see cref="LibVLCSharp.Shared.MediaPlayer.SetAudioTrack"/> directly when the user
    ///   changes a track selection during playback. Skipped on initial load so VLC's own
    ///   audio pipeline is not disturbed before it is fully initialised.</item>
    ///   <item><see cref="AudioMode.MixedRemux"/> — sets <see cref="HasPendingAudioChanges"/>
    ///   so the user can trigger the mix via <see cref="ApplyAudioMixCommand"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="token">Cancellation token from the debounce scheduler.</param>
    /// <param name="isInitialLoad">
    /// <see langword="true"/> when called from <see cref="RefreshAudioTracksAsync"/> on first
    /// play. Skips <c>SetAudioTrack</c> calls so that VLC's audio pipeline (which may not yet
    /// be fully initialised at the <c>Playing</c> event boundary) is not disturbed.
    /// </param>
    private async Task ApplyAudioRoutingAsync(
        System.Threading.CancellationToken token,
        bool isInitialLoad = false)
    {
        if (_clip is null || AudioTracks.Count == 0) return;

        var tracks = AudioTracks
            .Select(t => new ClipStudio.Application.Models.TrackMixInfo(
                t.TrackIndex, t.IsIncluded, t.Volume, t.FfmpegStreamIndex))
            .ToList();

        var includedCount = tracks.Count(t => t.IsIncluded);

        // 0 tracks included — mute VLC (intentional, always applied even on initial load).
        if (includedCount == 0)
        {
            MediaPlayer.SetAudioTrack(-1);
            HasPendingAudioChanges = false;
            IsPlayingMixPreview    = false;
            _audioMode             = AudioMode.NativeSingleTrack;
            return;
        }

        var needsMix = _mixedAudioService.ShouldUseMix(tracks);
        var prevMode = _audioMode;
        _audioMode   = needsMix ? AudioMode.MixedRemux : AudioMode.NativeSingleTrack;

        if (!needsMix)
        {
            HasPendingAudioChanges = false;

            var included = AudioTracks.Where(t => t.IsIncluded).ToList();
            var trackId  = included.Count == 1
                ? included[0].TrackIndex
                : AudioTracks[0].TrackIndex; // all-unity — VLC default (first track)

            if (prevMode == AudioMode.MixedRemux)
            {
                // Reload the original file to drop the remux preview, then apply track selection.
                _pendingNativeTrackId = trackId;
                ReloadMedia(_clip.FilePath);
                IsPlayingMixPreview = false;
            }
            else if (!isInitialLoad)
            {
                // Apply the track selection immediately. Skipped on initial load because
                // calling SetAudioTrack from the Playing-event dispatcher callback can
                // silently break VLC's audio output on Windows before the pipeline is ready.
                MediaPlayer.SetAudioTrack(trackId);
                IsPlayingMixPreview = false;
            }

            return;
        }

        // MixedRemux path.
        if (isInitialLoad)
        {
            // On first play: if a cached mix exists, load it automatically so the user hears
            // the mix they configured last time without pressing Apply Mix.
            // If no cache exists (or cache is disabled), generate it silently in the background.
            HasPendingAudioChanges = false;

            if (_settingsService.Current.CacheAudioPreviews)
            {
                // Check both slots and use the most recently written file (session slot tracking
                // is lost between close/open, so we must inspect the filesystem).
                var path0 = GetAudioMixCachePath(0);
                var path1 = GetAudioMixCachePath(1);
                var exists0 = System.IO.File.Exists(path0);
                var exists1 = System.IO.File.Exists(path1);

                string? cachedPath = null;
                if (exists0 && exists1)
                {
                    // Both slots present — prefer the most recently written.
                    cachedPath     = System.IO.File.GetLastWriteTimeUtc(path0) >= System.IO.File.GetLastWriteTimeUtc(path1)
                        ? path0 : path1;
                    _activeMixSlot = cachedPath == path0 ? 0 : 1;
                }
                else if (exists0)
                {
                    cachedPath     = path0;
                    _activeMixSlot = 0;
                }
                else if (exists1)
                {
                    cachedPath     = path1;
                    _activeMixSlot = 1;
                }

                if (cachedPath is not null)
                {
                    _mixedPreviewPath = cachedPath;
                    ReloadMedia(cachedPath);
                    IsPlayingMixPreview = true;
                    return;
                }
            }

            // No usable cache — generate silently in the background.
            _ = ApplyAudioMixAsync();
        }
        else
        {
            // User-initiated change: show the Apply Mix button so they can preview before committing.
            HasPendingAudioChanges = true;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Generates the FFmpeg remux preview for the current audio track configuration and
    /// reloads VLC with the preview file. Called by <see cref="ApplyAudioMixCommand"/> and
    /// fire-and-forget from <see cref="ApplyAudioRoutingAsync"/> on initial load.
    /// Uses alternating cache slots so FFmpeg never writes to the file VLC currently has open.
    /// Cancels any in-progress generation before starting a new one.
    /// </summary>
    private async Task ApplyAudioMixAsync()
    {
        if (_clip is null || AudioTracks.Count == 0) return;

        // Cancel any in-progress generation before starting a new one.
        _mixApplyCts?.Cancel();
        _mixApplyCts?.Dispose();
        _mixApplyCts = new System.Threading.CancellationTokenSource();
        var token = _mixApplyCts.Token;

        IsMixApplying          = true;
        HasPendingAudioChanges = false;

        var tracks = AudioTracks
            .Select(t => new ClipStudio.Application.Models.TrackMixInfo(
                t.TrackIndex, t.IsIncluded, t.Volume, t.FfmpegStreamIndex))
            .ToList();

        // Write to the slot that VLC is NOT currently playing to avoid a Windows file-lock conflict.
        var nextSlot = 1 - _activeMixSlot;
        var mkvPath  = GetAudioMixCachePath(nextSlot);

        try
        {
            await _mixedAudioService.GenerateRemuxAsync(_clip.FilePath, tracks, mkvPath, token);

            // Bail out if this request was superseded while FFmpeg was running.
            token.ThrowIfCancellationRequested();

            // Commit the new slot and reload VLC with the freshly written file.
            _activeMixSlot    = nextSlot;
            _mixedPreviewPath = mkvPath;
            ReloadMedia(mkvPath);
            IsPlayingMixPreview = true;
        }
        catch (OperationCanceledException)
        {
            // A newer request superseded this one — leave player state as-is.
        }
        catch (Exception ex)
        {
            // Restore pending flag so the user can retry.
            HasPendingAudioChanges = true;
            // Ensure VLC is on a known-good track (original file is still loaded).
            if (AudioTracks.Count > 0)
                MediaPlayer.SetAudioTrack(AudioTracks[0].TrackIndex);
            System.Diagnostics.Debug.WriteLine($"[ClipStudio] ApplyAudioMixAsync failed: {ex.Message}");
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsMixApplying = false;
        }
    }

    /// <summary>
    /// Returns the per-clip audio mix cache path for the given slot.
    /// Slot 0 (default) → <c>clip_{id}_audio_preview.mkv</c>;
    /// slot 1 → <c>clip_{id}_audio_preview_alt.mkv</c>.
    /// Using two alternating slots ensures FFmpeg never tries to overwrite the file that
    /// VLC currently has open (locked on Windows).
    /// </summary>
    /// <param name="slot">
    /// The cache slot index. Pass -1 (default) to use <see cref="_activeMixSlot"/>.
    /// </param>
    private string GetAudioMixCachePath(int slot = -1)
    {
        var folder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipStudio", "audio_cache");
        System.IO.Directory.CreateDirectory(folder);
        var slotToUse = slot < 0 ? _activeMixSlot : slot;
        var suffix    = slotToUse == 0 ? string.Empty : "_alt";
        return System.IO.Path.Combine(folder, $"clip_{_clip!.Id}_audio_preview{suffix}.mkv");
    }

    /// <summary>
    /// Saves the current playback position, rebuilds the VLC <see cref="Media"/> from
    /// <paramref name="path"/>, and resumes playback. The saved position is applied on the next
    /// <c>Playing</c> event via <see cref="OnPlayerPlaying"/>. Unlike the old input-slave approach,
    /// no slave options are needed: <paramref name="path"/> is the complete media file to open.
    /// </summary>
    /// <param name="path">
    /// The absolute path to the media file to open — either the original clip or the remux preview MKV.
    /// </param>
    private void ReloadMedia(string path)
    {
        // Clamp to 0: MediaPlayer.Time returns -1 when no media is playing.
        _mixReloadSeekMs = Math.Max(0, MediaPlayer.Time);

        _media?.Dispose();
        _media = new Media(_libVlc, path, FromType.FromPath);

        MediaPlayer.Media = _media;
        MediaPlayer.Play();
    }

    private void TogglePlayPause()
    {
        if (MediaPlayer.IsPlaying)
            MediaPlayer.Pause();
        else
            MediaPlayer.Play();
    }

    private void SeekRelative(TimeSpan offset)
    {
        if (IsWatchMode)
        {
            var watchStartMs = (long)WatchStart.TotalMilliseconds;
            var watchEndMs   = (long)WatchEnd.TotalMilliseconds;
            var newMs = Math.Max(watchStartMs,
                Math.Min(watchEndMs, MediaPlayer.Time + (long)offset.TotalMilliseconds));
            MediaPlayer.Time = newMs;
            UpdatePositionDisplay(TimeSpan.FromMilliseconds(newMs - watchStartMs));
            return;
        }

        var absNewMs = Math.Max(0,
            Math.Min((long)(DurationSeconds * 1000),
                MediaPlayer.Time + (long)offset.TotalMilliseconds));
        MediaPlayer.Time = absNewMs;
        UpdatePositionDisplay(TimeSpan.FromMilliseconds(absNewMs));
    }

    /// <summary>
    /// Updates <see cref="PositionSeconds"/> and <see cref="PositionDisplay"/> from a known time value
    /// without triggering a redundant seek via <see cref="OnPositionSecondsChanged"/>.
    /// Required when VLC is paused and will not raise a <c>TimeChanged</c> event.
    /// </summary>
    /// <param name="ts">The target position.</param>
    private void UpdatePositionDisplay(TimeSpan ts)
    {
        _isUpdatingFromPlayer = true;
        PositionSeconds = ts.TotalSeconds;
        PositionDisplay = FormatTime(ts);
        _isUpdatingFromPlayer = false;
    }

    private void BeginAddHighlight()
    {
        // Mutual exclusion: close the trim form so both handle-sets never appear simultaneously.
        if (IsTrimming)
        {
            IsTrimming             = false;
            TrimDestructiveWarning = null;
        }

        IsAddingHighlight = true;
    }

    private void MarkHighlightStart()
    {
        _highlightStart       = TimeSpan.FromMilliseconds(MediaPlayer.Time);
        HighlightStartDisplay = FormatTime(_highlightStart);
        OnPropertyChanged(nameof(HighlightStartFraction));
    }

    private void MarkHighlightEnd()
    {
        _highlightEnd       = TimeSpan.FromMilliseconds(MediaPlayer.Time);
        HighlightEndDisplay = FormatTime(_highlightEnd);
        OnPropertyChanged(nameof(HighlightEndFraction));
    }

    /// <summary>
    /// Sets the highlight start time from a proportional canvas position dragged by the user.
    /// Called from the view code-behind drag handler for the start handle.
    /// </summary>
    /// <param name="fraction">Horizontal fraction in [0, 1] relative to the canvas width.</param>
    public void SetHighlightStartFromFraction(double fraction)
    {
        _highlightStart       = TimeSpan.FromSeconds(Math.Clamp(fraction * DurationSeconds, 0, DurationSeconds));
        HighlightStartDisplay = FormatTime(_highlightStart);
        OnPropertyChanged(nameof(HighlightStartFraction));
    }

    /// <summary>
    /// Sets the highlight end time from a proportional canvas position dragged by the user.
    /// Called from the view code-behind drag handler for the end handle.
    /// </summary>
    /// <param name="fraction">Horizontal fraction in [0, 1] relative to the canvas width.</param>
    public void SetHighlightEndFromFraction(double fraction)
    {
        _highlightEnd       = TimeSpan.FromSeconds(Math.Clamp(fraction * DurationSeconds, 0, DurationSeconds));
        HighlightEndDisplay = FormatTime(_highlightEnd);
        OnPropertyChanged(nameof(HighlightEndFraction));
    }

    /// <summary>
    /// Sets the trim start time from a proportional canvas position dragged by the user.
    /// Called from the view code-behind drag handler for the trim-start handle.
    /// </summary>
    /// <param name="fraction">Horizontal fraction in [0, 1] relative to the canvas width.</param>
    public void SetTrimStartFromFraction(double fraction)
    {
        _trimStart       = TimeSpan.FromSeconds(Math.Clamp(fraction * DurationSeconds, 0, DurationSeconds));
        TrimStartDisplay = FormatTime(_trimStart);
        TrimStartError   = null;
        OnPropertyChanged(nameof(TrimStartFraction));
    }

    /// <summary>
    /// Sets the trim end time from a proportional canvas position dragged by the user.
    /// Called from the view code-behind drag handler for the trim-end handle.
    /// </summary>
    /// <param name="fraction">Horizontal fraction in [0, 1] relative to the canvas width.</param>
    public void SetTrimEndFromFraction(double fraction)
    {
        _trimEnd       = TimeSpan.FromSeconds(Math.Clamp(fraction * DurationSeconds, 0, DurationSeconds));
        TrimEndDisplay = FormatTime(_trimEnd);
        TrimEndError   = null;
        OnPropertyChanged(nameof(TrimEndFraction));
    }

    private void ResetHighlightForm()
    {
        IsAddingHighlight   = false;
        NewHighlightLabel   = string.Empty;
        _highlightStart     = TimeSpan.Zero;
        _highlightEnd       = TimeSpan.Zero;
        HighlightStartDisplay = "0:00";
        HighlightEndDisplay   = "0:00";
        OnPropertyChanged(nameof(HighlightStartFraction));
        OnPropertyChanged(nameof(HighlightEndFraction));
    }

    private async Task SaveHighlightAsync()
    {
        if (_clip is null || _highlightStart >= _highlightEnd)
            return;

        await _highlightService.CreateAsync(
            _clip.Id,
            _highlightStart,
            _highlightEnd,
            string.IsNullOrWhiteSpace(NewHighlightLabel) ? null : NewHighlightLabel.Trim());

        ResetHighlightForm();
        await RefreshHighlightsAsync();
        _soundService.Play(SoundEffect.HighlightCreated);
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
    }

    private async Task TakeScreenshotAsync()
    {
        if (_clip is null) return;
        var position = TimeSpan.FromMilliseconds(MediaPlayer.Time);
        await _screenshotService.CaptureAsync(_clip.Id, position);
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

    // ---- Trim & Export ----

    private void BeginTrim()
    {
        if (IsWatchMode) return;

        // Mutual exclusion: close the highlight form so both handle-sets never appear simultaneously.
        if (IsAddingHighlight) ResetHighlightForm();

        if (_clip is not null && string.IsNullOrEmpty(TrimOutputPath))
        {
            var dir      = Path.GetDirectoryName(_clip.FilePath) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(_clip.FileName);
            TrimOutputPath = Path.Combine(dir, $"{baseName}_trimmed.mp4");
        }

        IsTrimDestructive = _settingsService.Current.DefaultTrimMode == TrimMode.Destructive;
        _trimStart       = TimeSpan.Zero;
        _trimEnd         = _clip?.Duration ?? TimeSpan.Zero;
        TrimStartDisplay = FormatTime(_trimStart);
        TrimEndDisplay   = FormatTime(_trimEnd);
        OnPropertyChanged(nameof(TrimStartFraction));
        OnPropertyChanged(nameof(TrimEndFraction));
        IsTrimming       = true;
    }

    private void MarkTrimStart()
    {
        _trimStart       = TimeSpan.FromMilliseconds(MediaPlayer.Time);
        TrimStartDisplay = FormatTime(_trimStart);
        OnPropertyChanged(nameof(TrimStartFraction));
    }

    private void MarkTrimEnd()
    {
        _trimEnd       = TimeSpan.FromMilliseconds(MediaPlayer.Time);
        TrimEndDisplay = FormatTime(_trimEnd);
        OnPropertyChanged(nameof(TrimEndFraction));
    }

    /// <summary>
    /// Parses the current <see cref="TrimStartDisplay"/> text and updates the underlying trim start.
    /// Resets the display to the last valid value and sets <see cref="TrimStartError"/> on failure.
    /// </summary>
    private void CommitTrimStart()
    {
        if (TryParseTime(TrimStartDisplay, out var ts))
        {
            _trimStart      = ts;
            TrimStartError  = null;
            TrimStartDisplay = FormatTime(_trimStart);
            OnPropertyChanged(nameof(TrimStartFraction));
        }
        else
        {
            TrimStartError   = "Invalid time (use m:ss or h:mm:ss).";
            TrimStartDisplay = FormatTime(_trimStart);
        }
    }

    /// <summary>
    /// Parses the current <see cref="TrimEndDisplay"/> text and updates the underlying trim end.
    /// Resets the display to the last valid value and sets <see cref="TrimEndError"/> on failure.
    /// </summary>
    private void CommitTrimEnd()
    {
        if (TryParseTime(TrimEndDisplay, out var ts))
        {
            _trimEnd      = ts;
            TrimEndError  = null;
            TrimEndDisplay = FormatTime(_trimEnd);
            OnPropertyChanged(nameof(TrimEndFraction));
        }
        else
        {
            TrimEndError   = "Invalid time (use m:ss or h:mm:ss).";
            TrimEndDisplay = FormatTime(_trimEnd);
        }
    }

    /// <summary>
    /// Attempts to parse a user-entered time string in <c>m:ss</c> or <c>h:mm:ss</c> format.
    /// </summary>
    /// <param name="input">The raw input string.</param>
    /// <param name="result">The parsed <see cref="TimeSpan"/>, or <see cref="TimeSpan.Zero"/> on failure.</param>
    /// <returns><see langword="true"/> if parsing succeeded; otherwise <see langword="false"/>.</returns>
    private static bool TryParseTime(string? input, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input)) return false;

        if (TimeSpan.TryParseExact(input.Trim(), [@"m\:ss", @"h\:mm\:ss", @"mm\:ss"], null, out result))
            return true;

        // Fallback: plain seconds as integer.
        if (int.TryParse(input.Trim(), out var secs))
        {
            result = TimeSpan.FromSeconds(secs);
            return true;
        }

        return false;
    }

    private async Task QueueTrimExportAsync()
    {
        if (_clip is null || _trimStart >= _trimEnd || string.IsNullOrWhiteSpace(TrimOutputPath))
            return;

        // CE4: warn when destructive trim would clip highlights that fall outside the trim range.
        if (IsTrimDestructive)
        {
            var outside = Highlights
                .Where(h => h.StartTime < _trimStart || h.EndTime > _trimEnd)
                .ToList();

            if (outside.Count > 0)
            {
                var names = string.Join(", ", outside.Take(3).Select(h => $"\"{h.Label}\""));
                if (outside.Count > 3) names += $" and {outside.Count - 3} more";
                TrimDestructiveWarning =
                    $"{outside.Count} highlight{(outside.Count == 1 ? "" : "s")} " +
                    $"fall{(outside.Count == 1 ? "s" : "")} outside the trim range and will be clipped: {names}. Queue anyway?";
                return;
            }
        }

        await ExecuteQueueTrimAsync();
    }

    /// <summary>
    /// Confirms a destructive trim export after the user acknowledges the highlight-outside-range warning.
    /// Bypasses the warning check and proceeds to queue immediately.
    /// </summary>
    private async Task ConfirmDestructiveTrimAsync()
    {
        TrimDestructiveWarning = null;
        await ExecuteQueueTrimAsync();
    }

    /// <summary>
    /// Queues the trim export unconditionally. Called by <see cref="QueueTrimExportAsync"/>
    /// when no warning applies, and by <see cref="ConfirmDestructiveTrimAsync"/> after user confirmation.
    /// </summary>
    private async Task ExecuteQueueTrimAsync()
    {
        if (_clip is null) return;

        var trimMode = IsTrimDestructive ? TrimMode.Destructive : TrimMode.NonDestructive;
        await _exportService.QueueAsync(
            _clip.Id,
            null,
            TrimOutputPath,
            trimMode,
            IsTrimDestructive,
            _trimStart,
            _trimEnd);

        IsTrimming = false;
    }

    /// <summary>Clears the destructive-trim warning whenever the trim form is closed.</summary>
    partial void OnIsTrimmingChanged(bool value)
    {
        if (!value) TrimDestructiveWarning = null;
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
        if (_isDragging) return;

        // Discard stale pre-seek events that VLC fires after a programmatic seek.
        if (_ignoreTimeChangedBeforeMs >= 0)
        {
            if (e.Time < _ignoreTimeChangedBeforeMs)
                return;
            _ignoreTimeChangedBeforeMs = -1; // first valid event arrived; resume normal updates
        }

        Dispatcher.UIThread.Post(() =>
        {
            var ts = TimeSpan.FromMilliseconds(e.Time);

            if (IsWatchMode)
            {
                // When the watched highlight's end is reached, behaviour depends on LoopMode.
                if (ts >= WatchEnd)
                {
                    switch (LoopMode)
                    {
                        case LoopMode.LoopThis:
                            // Loop back to the start of this highlight.
                            _ignoreTimeChangedBeforeMs = (long)WatchStart.TotalMilliseconds - 200;
                            MediaPlayer.Time = (long)WatchStart.TotalMilliseconds;
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
                            _ignoreTimeChangedBeforeMs = (long)WatchStart.TotalMilliseconds - 200;
                            MediaPlayer.Time = (long)WatchStart.TotalMilliseconds;
                            _isUpdatingFromPlayer = true;
                            PositionSeconds = 0;
                            PositionDisplay = FormatTime(TimeSpan.Zero);
                            _isUpdatingFromPlayer = false;
                            break;
                    }
                    return;
                }

                // Display position relative to the highlight start.
                _isUpdatingFromPlayer = true;
                var relativeTs = ts > WatchStart ? ts - WatchStart : TimeSpan.Zero;
                PositionSeconds = relativeTs.TotalSeconds;
                PositionDisplay = FormatTime(relativeTs);
                _isUpdatingFromPlayer = false;
                return;
            }

            _isUpdatingFromPlayer = true;
            PositionSeconds = ts.TotalSeconds;
            PositionDisplay = FormatTime(ts);
            _isUpdatingFromPlayer = false;

            // Highlight loop: if a highlight is locked and the position has passed its end,
            // seek back only when looping is enabled.
            if (LockedHighlight is not null && ts >= LockedHighlight.EndTime
                && LoopMode != LoopMode.Off)
                MediaPlayer.Time = (long)LockedHighlight.StartTime.TotalMilliseconds;
        });
    }

    /// <summary>
    /// Writes a diagnostic snapshot of the current VLC audio state to
    /// <c>%TEMP%\clipstudio_audio.log</c>. Remove once the audio issue is resolved.
    /// </summary>
    private void LogAudioDiagnostics(string context)
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
            IsPlaying = true;
            LogAudioDiagnostics("OnPlayerPlaying");

            // After a media reload (remux preview or clean-reload for mode transition), seek back
            // to the position saved before the reload. AudioTracks is already populated so
            // RefreshAudioTracksAsync is skipped by the Count == 0 guard below.
            if (_mixReloadSeekMs >= 0)
            {
                var seekTarget             = _mixReloadSeekMs;
                _mixReloadSeekMs           = -1;
                _ignoreTimeChangedBeforeMs = seekTarget - 200;

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
            if (AudioTracks.Count == 0)
                _ = RefreshAudioTracksAsync();

            // After end-of-clip with loop off: immediately pause at position 0 so the user
            // can replay by pressing play or scrubbing without needing to reload the clip.
            if (_replayAfterEnd)
            {
                _replayAfterEnd = false;
                MediaPlayer.Pause();
                _isUpdatingFromPlayer = true;
                PositionSeconds = 0;
                PositionDisplay = FormatTime(TimeSpan.Zero);
                _isUpdatingFromPlayer = false;
                return;
            }

            // In watch mode with LoopOff: the clip reached its natural end at WatchEnd.
            // Seek back to WatchStart then pause so the user can replay the highlight.
            if (_watchModeEndPending)
            {
                _watchModeEndPending = false;
                _ignoreTimeChangedBeforeMs = (long)WatchStart.TotalMilliseconds - 200;
                MediaPlayer.Time = (long)WatchStart.TotalMilliseconds;
                MediaPlayer.Pause();
                _isUpdatingFromPlayer = true;
                PositionSeconds = 0;
                PositionDisplay = FormatTime(TimeSpan.Zero);
                _isUpdatingFromPlayer = false;
                return;
            }

            // In watch mode: seek to the highlight start as soon as playback starts.
            if (_watchModeSeekPending)
            {
                _watchModeSeekPending = false;
                _ignoreTimeChangedBeforeMs = (long)WatchStart.TotalMilliseconds - 200;
                MediaPlayer.Time = (long)WatchStart.TotalMilliseconds;
            }
        });

    private void OnPlayerPaused(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => IsPlaying = false);

    private void OnPlayerStopped(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() => IsPlaying = false);

    private void OnPlayerEndReached(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;

            if (IsWatchMode)
            {
                switch (LoopMode)
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

            switch (LoopMode)
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

    /// <summary>
    /// Called by the source generator when <see cref="PositionSeconds"/> changes.
    /// When the change originates from user interaction (not the player and not an active drag),
    /// seeks the player and sets the stale-event ignore threshold.
    /// During an active drag (<see cref="_isDragging"/> is true) the seek is deferred to
    /// <see cref="EndScrub"/> so that VLC is not flooded with intermediate seek requests.
    /// </summary>
    partial void OnPositionSecondsChanged(double value)
    {
        if (_isUpdatingFromPlayer || _isDragging) return;
        var targetMs = (long)(value * 1000);
        _ignoreTimeChangedBeforeMs = targetMs - 200;
        MediaPlayer.Time = targetMs;
    }

    /// <summary>Called by the source generator when <see cref="LoopMode"/> changes.</summary>
    partial void OnLoopModeChanged(LoopMode value)
    {
        OnPropertyChanged(nameof(IsLoopOff));
        OnPropertyChanged(nameof(IsLoopThis));
        OnPropertyChanged(nameof(IsLoopAll));
    }

    /// <summary>
    /// Advances <see cref="LoopMode"/> through the cycle Off → LoopThis → LoopAll → Off.
    /// </summary>
    private void CycleLoopMode()
    {
        LoopMode = LoopMode switch
        {
            LoopMode.Off      => LoopMode.LoopThis,
            LoopMode.LoopThis => LoopMode.LoopAll,
            _                 => LoopMode.Off,
        };
    }

    private static string FormatTime(TimeSpan ts) =>
        ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

    // ---- IDisposable ----

    /// <inheritdoc/>
    public void Dispose()
    {
        _mixDebounce?.Cancel();
        _mixDebounce?.Dispose();
        _mixDebounce = null;

        _mixApplyCts?.Cancel();
        _mixApplyCts?.Dispose();
        _mixApplyCts = null;

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
