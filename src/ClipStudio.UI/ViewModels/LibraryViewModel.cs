using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the main clip library view.
/// Displays all clips as a card grid or details list and exposes search, filter,
/// sort, view-mode toggle, clip-open, and staged bulk-edit capabilities.
/// </summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    private readonly IClipService _clipService;
    private readonly ITagService _tagService;
    private readonly IPlayerService _playerService;
    private readonly IFilterPresetService _filterPresetService;

    // ---- Copy-format source ----
    private ClipCardViewModel? _copyFormatSource;

    // ---- Staged bulk tags: IDs explicitly added/promoted by the user this session ----
    private readonly HashSet<int> _stagedNewTagIds    = new();
    private readonly HashSet<int> _stagedNewPlayerIds = new();
    private int? _stagedNewGameId;

    // ---- Optimistically removed IDs (immediate DB removal, hidden from chip list) ----
    private readonly HashSet<int> _bulkRemovedTagIds    = new();
    private readonly HashSet<int> _bulkRemovedPlayerIds = new();
    private int? _bulkRemovedGameTagId;

    // ---- Load cancellation + picker-reload suppression ----
    private CancellationTokenSource _loadCts = new();
    private bool _suppressFilterChanges;

    // ---- Bulk transcription ----
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settingsService;
    private CancellationTokenSource? _bulkTranscribeCts;

    // ---- Preset filter IDs applied on the next LoadAsync call ----
    private int? _presetGameTagId;
    private int? _presetTagId;
    private int? _presetPlayerId;

    // ---- Scroll + last-visited state (persists across detail-view navigation) ----
    private int _lastOpenedClipId;
    private double _tilesScrollOffsetY;

    /// <summary>Gets the observable collection of clip cards shown in the library.</summary>
    public ObservableCollection<ClipCardViewModel> Clips { get; } = new();

    /// <summary>Gets a value indicating whether any loaded clip has a missing source file.</summary>
    public bool HasBrokenClips => Clips.Any(c => c.IsBroken);

    /// <summary>Gets a value indicating whether any currently selected clip has a missing source file.</summary>
    public bool HasSelectedBrokenClips => SelectedClips.Any(c => c.IsBroken);

    // ---- Multi-select ----

    /// <summary>Gets the ordered collection of currently selected clips.</summary>
    public ObservableCollection<ClipCardViewModel> SelectedClips { get; } = new();

    /// <summary>Gets a value indicating whether at least one clip is selected.</summary>
    public bool HasSelectedClips => SelectedClips.Count > 0;

    /// <summary>Gets a value indicating whether multi-select mode is active (any clip selected).</summary>
    public bool IsMultiSelectMode => SelectedClips.Count > 0;

    /// <summary>
    /// Gets or sets whether the bulk edit panel is visible.
    /// Can be toggled from the toolbar regardless of selection state.
    /// Auto-opens when the first clip is selected.
    /// </summary>
    [ObservableProperty] private bool _isBulkEditPanelOpen;

    /// <summary>
    /// Gets a value indicating whether the copy-format button should be enabled.
    /// True only when exactly one clip is selected and copy-format mode is not already active.
    /// </summary>
    public bool IsCopyFormatButtonEnabled => SelectedClips.Count == 1 && !IsCopyFormatMode;

    /// <summary>Gets a display string such as "3 clips selected".</summary>
    public string SelectedClipsCountDisplay =>
        $"{SelectedClips.Count} clip{(SelectedClips.Count == 1 ? "" : "s")} selected";

    /// <summary>Gets or sets a value indicating whether copy-format mode is active.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCopyFormatButtonEnabled))]
    private bool _isCopyFormatMode;

    /// <summary>Gets the name of the clip that was copied (used as a label in the paste UI).</summary>
    [ObservableProperty] private string _copyFormatSourceDisplay = string.Empty;

    // ---- Staged bulk-edit pending chips ----

    /// <summary>Gets the staged tag chips for the current bulk-edit session.</summary>
    public ObservableCollection<BulkTagChipViewModel> BulkPendingTags    { get; } = new();

    /// <summary>Gets the staged player chips for the current bulk-edit session.</summary>
    public ObservableCollection<BulkTagChipViewModel> BulkPendingPlayers { get; } = new();

    /// <summary>Gets the staged game chips for the current bulk-edit session (at most one New chip).</summary>
    public ObservableCollection<BulkTagChipViewModel> BulkPendingGames   { get; } = new();

    // ---- Per-category apply toggles ----

    /// <summary>Gets or sets whether the Apply command will apply pending New tag chips.</summary>
    [ObservableProperty] private bool _bulkApplyTags    = true;

    /// <summary>Gets or sets whether the Apply command will apply the pending New player chips.</summary>
    [ObservableProperty] private bool _bulkApplyPlayers = true;

    /// <summary>Gets or sets whether the Apply command will apply the pending New game chip.</summary>
    [ObservableProperty] private bool _bulkApplyGame    = true;

    // ---- Copy-format paste aspect toggles ----

    /// <summary>Gets or sets whether copy-format paste will apply tags.</summary>
    [ObservableProperty] private bool _bulkCopyPasteTags    = true;

    /// <summary>Gets or sets whether copy-format paste will apply players.</summary>
    [ObservableProperty] private bool _bulkCopyPastePlayers = true;

    /// <summary>Gets or sets whether copy-format paste will apply the game.</summary>
    [ObservableProperty] private bool _bulkCopyPasteGame    = true;

    // ---- Bulk pickers (for adding to staging) ----

    /// <summary>Gets or sets the tag selected in the bulk tag-add picker.</summary>
    [ObservableProperty] private Tag? _bulkSelectedTag;

    /// <summary>Gets or sets the player selected in the bulk player-add picker.</summary>
    [ObservableProperty] private Player? _bulkSelectedPlayerPicker;

    /// <summary>Gets or sets the game tag selected in the bulk game picker.</summary>
    [ObservableProperty] private Tag? _bulkSelectedGamePicker;

    /// <summary>Gets or sets whether the bulk delete confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isBulkDeleteConfirmVisible;

    /// <summary>Gets or sets whether the "remove all broken clips" toolbar confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isRemoveAllBrokenConfirmVisible;

    /// <summary>Gets or sets a transient status or error message shown in the library toolbar.</summary>
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Gets or sets whether the "remove all tags from selection" confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isRemoveAllTagsConfirmVisible;

    /// <summary>Gets or sets whether the "remove all players from selection" confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isRemoveAllPlayersConfirmVisible;

    /// <summary>Gets or sets whether the "remove game from selection" confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isRemoveAllGameConfirmVisible;

    /// <summary>Gets or sets whether the "clear all data from selection" confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isBulkClearAllDataConfirmVisible;

    /// <summary>Gets or sets a value indicating that the library is currently loading.</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>Gets or sets the free-text search string used to filter displayed clips.</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the search should also look inside transcription
    /// segment text.  Disabled by default to avoid the extra DB query on every keystroke.
    /// </summary>
    [ObservableProperty] private bool _searchCaptions = false;

    /// <summary>Gets or sets whether a bulk transcription run is currently in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBulkTranscribe))]
    private bool _isBulkTranscribing;

    /// <summary>Gets or sets the bulk transcription progress (0–1).</summary>
    [ObservableProperty] private float _bulkTranscribeProgress;

    /// <summary>Gets or sets the status message shown in the bulk transcription section.</summary>
    [ObservableProperty] private string _bulkTranscribeStatus = string.Empty;

    /// <summary>
    /// Gets whether transcription is enabled in settings and the bulk transcribe button may appear.
    /// Refreshed on every <see cref="LoadAsync"/> call.
    /// </summary>
    public bool IsTranscriptionEnabled => _settingsService.Current.TranscriptionEnabled;

    /// <summary>Gets whether the bulk transcribe command can execute.</summary>
    public bool CanBulkTranscribe => HasSelectedClips && !IsBulkTranscribing;

    /// <summary>
    /// Gets or sets the row height in pixels applied to each details-view row.
    /// Persisted to <see cref="ClipStudio.Application.Models.AppSettings.LibraryDetailsRowHeight"/>.
    /// </summary>
    [ObservableProperty] private int _detailsRowHeight = 52;

    // ---- View mode ----

    /// <summary>Gets or sets the current view mode: "Tiles" or "Details".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTilesView))]
    [NotifyPropertyChangedFor(nameof(IsDetailsView))]
    private string _viewMode = "Tiles";

    /// <summary>Gets a value indicating whether the tiles view is active.</summary>
    public bool IsTilesView => ViewMode == "Tiles";

    /// <summary>Gets a value indicating whether the details view is active.</summary>
    public bool IsDetailsView => ViewMode == "Details";

    // ---- Sort ----

    /// <summary>
    /// Gets or sets the current sort key. Accepted values:
    /// "DateDesc", "DateAsc", "NameAsc", "NameDesc", "DurationDesc", "DurationAsc", "RatingDesc", "RatingAsc".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortIndicatorForName))]
    [NotifyPropertyChangedFor(nameof(SortIndicatorForDate))]
    [NotifyPropertyChangedFor(nameof(SortIndicatorForDuration))]
    [NotifyPropertyChangedFor(nameof(SortIndicatorForRating))]
    private string _sortBy = "DateDesc";

    /// <summary>Gets a sort direction indicator for the Name column: "▲", "▼", or empty.</summary>
    public string SortIndicatorForName     => SortBy is "NameAsc" ? "▲" : SortBy is "NameDesc" ? "▼" : "";

    /// <summary>Gets a sort direction indicator for the Date column: "▲", "▼", or empty.</summary>
    public string SortIndicatorForDate     => SortBy is "DateAsc" ? "▲" : SortBy is "DateDesc" ? "▼" : "";

    /// <summary>Gets a sort direction indicator for the Duration column: "▲", "▼", or empty.</summary>
    public string SortIndicatorForDuration => SortBy is "DurationAsc" ? "▲" : SortBy is "DurationDesc" ? "▼" : "";

    /// <summary>Gets a sort direction indicator for the Rating column: "▲", "▼", or empty.</summary>
    public string SortIndicatorForRating   => SortBy is "RatingAsc" ? "▲" : SortBy is "RatingDesc" ? "▼" : "";

    // ---- Filter panel ----

    /// <summary>Gets or sets a value indicating whether the filter panel is visible.</summary>
    [ObservableProperty] private bool _isFilterPanelOpen;

    /// <summary>Gets or sets the clip status filter. Null = all statuses.</summary>
    [ObservableProperty] private ClipStatus? _filterStatus;

    /// <summary>Gets or sets the earliest creation date filter (inclusive). Null = no lower bound.</summary>
    [ObservableProperty] private DateTime? _filterDateFrom;

    /// <summary>Gets or sets the latest creation date filter (inclusive). Null = no upper bound.</summary>
    [ObservableProperty] private DateTime? _filterDateTo;

    /// <summary>Gets or sets the game tag to filter by. Null = all games.</summary>
    [ObservableProperty] private Tag? _filterGameTag;

    /// <summary>Gets or sets whether only favourite clips are shown. Null = all clips.</summary>
    [ObservableProperty] private bool? _filterIsFavourite;

    /// <summary>Gets or sets the tag selected in the filter panel, or null for all tags.</summary>
    [ObservableProperty] private Tag? _selectedFilterTag;

    /// <summary>Gets or sets the player selected in the filter panel, or null for all players.</summary>
    [ObservableProperty] private Player? _selectedFilterPlayer;

    /// <summary>Gets the selected tag identifiers used to filter clips by tag.</summary>
    public ObservableCollection<int> SelectedTagIds { get; } = new();

    /// <summary>Gets the selected player identifiers used to filter clips by player.</summary>
    public ObservableCollection<int> SelectedPlayerIds { get; } = new();

    // ---- Multi-value chip filters ----

    /// <summary>Gets the active tag filter chips (each can be include or exclude).</summary>
    public ObservableCollection<FilterChipViewModel> FilterTagChips { get; } = new();

    /// <summary>Gets the active game filter chips (each can be include or exclude).</summary>
    public ObservableCollection<FilterChipViewModel> FilterGameChips { get; } = new();

    /// <summary>Gets the active player filter chips (each can be include or exclude).</summary>
    public ObservableCollection<FilterChipViewModel> FilterPlayerChips { get; } = new();

    // ---- Filter presets ----

    /// <summary>Gets the list of saved filter presets.</summary>
    public ObservableCollection<FilterPresetRowViewModel> SavedPresets { get; } = new();

    /// <summary>Gets or sets the name entered by the user for a new preset to save.</summary>
    [ObservableProperty] private string _savePresetName = string.Empty;

    /// <summary>Gets the command that saves the current filter state as a named preset.</summary>
    public IAsyncRelayCommand SaveFilterPresetCommand { get; }

    // ---- Picker data ----

    /// <summary>Gets the list of all general tags available for filtering and bulk editing.</summary>
    public ObservableCollection<Tag> AvailableTags { get; } = new();

    /// <summary>Gets the list of all game tags available for filtering and bulk editing.</summary>
    public ObservableCollection<Tag> AvailableGameTags { get; } = new();

    /// <summary>Gets the list of all players available for filtering and bulk editing.</summary>
    public ObservableCollection<Player> AvailablePlayers { get; } = new();

    /// <summary>Gets the available sort options shown in the sort ComboBox.</summary>
    public IReadOnlyList<(string Key, string Label)> SortOptions { get; } =
    [
        ("DateDesc",     "Date (newest first)"),
        ("DateAsc",      "Date (oldest first)"),
        ("NameAsc",      "Name (A-Z)"),
        ("NameDesc",     "Name (Z-A)"),
        ("DurationDesc", "Duration (longest first)"),
        ("DurationAsc",  "Duration (shortest first)"),
        ("RatingDesc",   "Rating (highest first)"),
    ];

    // ---- Commands ----

    /// <summary>Gets the command that loads or reloads the clip list.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that toggles the filter panel open or closed.</summary>
    public IRelayCommand ToggleFilterPanelCommand { get; }

    /// <summary>Gets the command that resets all filter fields to their defaults.</summary>
    public IRelayCommand ClearFiltersCommand { get; }

    /// <summary>Gets the command that toggles between Tiles and Details view modes.</summary>
    public IRelayCommand ToggleViewModeCommand { get; }

    // ---- Per-filter clear commands ----

    /// <summary>Gets the command that clears only the status filter.</summary>
    public IRelayCommand ClearStatusFilterCommand { get; }

    /// <summary>Gets the command that clears only the game filter.</summary>
    public IRelayCommand ClearGameFilterCommand { get; }

    /// <summary>Gets the command that clears only the date-from filter.</summary>
    public IRelayCommand ClearDateFromCommand { get; }

    /// <summary>Gets the command that clears only the date-to filter.</summary>
    public IRelayCommand ClearDateToCommand { get; }

    /// <summary>Gets the command that clears only the favourite filter.</summary>
    public IRelayCommand ClearFavouriteFilterCommand { get; }

    /// <summary>Gets the command that clears only the tag filter.</summary>
    public IRelayCommand ClearTagFilterCommand { get; }

    /// <summary>Gets the command that clears only the player filter.</summary>
    public IRelayCommand ClearPlayerFilterCommand { get; }

    /// <summary>Gets the command that sets the sort column, toggling between ascending and descending when the same column is clicked again.</summary>
    public IRelayCommand<string> SetSortCommand { get; }

    // ---- Row height / column-width commands ----

    /// <summary>Gets the command that increases the details row height by 8 pixels (up to 120).</summary>
    public IRelayCommand IncreaseRowHeightCommand { get; }

    /// <summary>Gets the command that decreases the details row height by 8 pixels (down to 32).</summary>
    public IRelayCommand DecreaseRowHeightCommand { get; }

    /// <summary>
    /// Gets the command that resets the details column widths to their defaults.
    /// Invoked from the column-header context menu in the view code-behind.
    /// </summary>
    public IRelayCommand ResetColumnWidthsCommand { get; }

    // ---- Bulk / multi-select commands ----

    /// <summary>Gets the command that selects all clips currently displayed in the library.</summary>
    public IRelayCommand SelectAllCommand { get; }

    /// <summary>Gets the command that deselects all currently selected clips.</summary>
    public IRelayCommand DeselectAllCommand { get; }

    /// <summary>Gets the command that toggles the bulk edit panel open or closed.</summary>
    public IRelayCommand ToggleBulkEditPanelCommand { get; }

    // ---- Staged tag commands ----

    /// <summary>Gets the command that stages the currently selected bulk tag as a New chip.</summary>
    public IRelayCommand AddBulkTagCommand { get; }

    /// <summary>Gets the command that stages the currently selected bulk player as a New chip.</summary>
    public IRelayCommand AddBulkPlayerCommand { get; }

    /// <summary>Gets the command that stages the currently selected bulk game as a New chip.</summary>
    public IRelayCommand AddBulkGameCommand { get; }

    /// <summary>Gets the command that clears all staged New tag chips (does not undo DB removals).</summary>
    public IRelayCommand ClearBulkTagsCommand { get; }

    /// <summary>Gets the command that clears all staged New player chips (does not undo DB removals).</summary>
    public IRelayCommand ClearBulkPlayersCommand { get; }

    /// <summary>Gets the command that clears the staged New game chip (does not undo DB removals).</summary>
    public IRelayCommand ClearBulkGameCommand { get; }

    /// <summary>Gets the command that applies all staged bulk edits to the selected clips.</summary>
    public IAsyncRelayCommand ApplyBulkEditsCommand { get; }

    /// <summary>Gets the command that discards all staged bulk edits without applying them.</summary>
    public IRelayCommand CancelBulkEditsCommand { get; }

    // ---- Trash ----

    /// <summary>Gets the command that shows the delete confirmation strip.</summary>
    public IRelayCommand ShowBulkDeleteConfirmCommand { get; }

    /// <summary>Gets the command that trashes all selected clips after confirmation.</summary>
    public IAsyncRelayCommand BulkTrashCommand { get; }

    /// <summary>Gets the command that shows the remove-all-broken confirmation strip.</summary>
    public IRelayCommand ShowRemoveAllBrokenConfirmCommand { get; }

    /// <summary>Gets the command that permanently removes all broken (missing-file) clips from the library after confirmation.</summary>
    public IAsyncRelayCommand ConfirmRemoveAllBrokenClipsCommand { get; }

    /// <summary>Gets the command that archives all selected broken clips.</summary>
    public IAsyncRelayCommand BulkArchiveBrokenCommand { get; }

    /// <summary>Gets the command that clears the transient status message from the toolbar.</summary>
    public IRelayCommand ClearStatusMessageCommand { get; }

    // ---- Destructive bulk remove per category ----

    /// <summary>Gets the command that shows the "remove all tags" confirmation strip.</summary>
    public IRelayCommand ShowRemoveAllTagsConfirmCommand { get; }

    /// <summary>Gets the command that removes all tags from all selected clips after confirmation.</summary>
    public IAsyncRelayCommand ConfirmRemoveAllTagsCommand { get; }

    /// <summary>Gets the command that shows the "remove all players" confirmation strip.</summary>
    public IRelayCommand ShowRemoveAllPlayersConfirmCommand { get; }

    /// <summary>Gets the command that removes all players from all selected clips after confirmation.</summary>
    public IAsyncRelayCommand ConfirmRemoveAllPlayersCommand { get; }

    /// <summary>Gets the command that shows the "remove game" confirmation strip.</summary>
    public IRelayCommand ShowRemoveAllGameConfirmCommand { get; }

    /// <summary>Gets the command that removes the game tag from all selected clips after confirmation.</summary>
    public IAsyncRelayCommand ConfirmRemoveAllGameCommand { get; }

    /// <summary>Gets the command that shows the "clear all data" confirmation strip.</summary>
    public IRelayCommand ShowBulkClearAllDataConfirmCommand { get; }

    /// <summary>
    /// Gets the command that clears all tags, players, rating and resets the status to Unreviewed
    /// for all selected clips after confirmation.
    /// </summary>
    public IAsyncRelayCommand ConfirmBulkClearAllDataCommand { get; }

    // ---- Bulk transcription commands ----

    /// <summary>
    /// Gets the command that transcribes all currently selected clips sequentially using the
    /// Whisper model configured in Settings. Disabled when no clips are selected or a run is already active.
    /// </summary>
    public IAsyncRelayCommand BulkTranscribeCommand { get; }

    /// <summary>Gets the command that cancels an in-progress bulk transcription run.</summary>
    public IRelayCommand CancelBulkTranscribeCommand { get; }

    // ---- Copy-format commands ----

    /// <summary>
    /// Gets the command that starts copy-format mode using the single selected clip as the source.
    /// Enabled only when exactly one clip is selected.
    /// </summary>
    public IRelayCommand StartCopyFormatCommand { get; }

    /// <summary>Gets the command that exits copy-format mode without applying any changes.</summary>
    public IRelayCommand ExitCopyFormatCommand { get; }

    /// <summary>
    /// Gets the command that pastes the copied format onto all currently selected clips,
    /// respecting the <see cref="BulkCopyPasteTags"/>, <see cref="BulkCopyPastePlayers"/>,
    /// and <see cref="BulkCopyPasteGame"/> toggles.
    /// </summary>
    public IAsyncRelayCommand PasteFormatToSelectionCommand { get; }

    /// <summary>
    /// Optional callback set by <see cref="MainWindowViewModel"/> to open a clip's detail view.
    /// Receives the clip ID, the full ordered sequence of clip IDs in the current view,
    /// and the index of the requested clip within that sequence.
    /// </summary>
    public Action<int, IReadOnlyList<int>, int>? ClipOpenRequested { get; set; }

    /// <summary>
    /// Raised by <see cref="ResetColumnWidthsCommand"/> to ask the view code-behind to clear
    /// persisted column widths and restore all columns to their AXAML defaults.
    /// </summary>
    public event Action? ColumnWidthsResetRequested;

    /// <summary>
    /// Initialises a new <see cref="LibraryViewModel"/>.
    /// </summary>
    public LibraryViewModel(
        IClipService clipService,
        ITagService tagService,
        IPlayerService playerService,
        IFilterPresetService filterPresetService,
        IServiceScopeFactory scopeFactory,
        ISettingsService settingsService)
    {
        _clipService          = clipService;
        _tagService           = tagService;
        _playerService        = playerService;
        _filterPresetService  = filterPresetService;
        _scopeFactory         = scopeFactory;
        _settingsService      = settingsService;

        LoadCommand                = new AsyncRelayCommand(LoadAsync);
        ToggleFilterPanelCommand   = new RelayCommand(() => IsFilterPanelOpen = !IsFilterPanelOpen);
        ClearFiltersCommand        = new RelayCommand(ClearFilters);
        ToggleViewModeCommand      = new RelayCommand(() => ViewMode = IsTilesView ? "Details" : "Tiles");
        ClearStatusFilterCommand   = new RelayCommand(() => FilterStatus     = null);
        ClearGameFilterCommand     = new RelayCommand(() => { FilterGameChips.Clear(); FilterGameTag = null; });
        ClearDateFromCommand       = new RelayCommand(() => FilterDateFrom   = null);
        ClearDateToCommand         = new RelayCommand(() => FilterDateTo     = null);
        ClearFavouriteFilterCommand = new RelayCommand(() => FilterIsFavourite  = null);
        ClearTagFilterCommand       = new RelayCommand(() => { FilterTagChips.Clear(); SelectedFilterTag = null; });
        ClearPlayerFilterCommand    = new RelayCommand(() => { FilterPlayerChips.Clear(); SelectedFilterPlayer = null; });
        SetSortCommand              = new RelayCommand<string>(SetSort);
        SaveFilterPresetCommand     = new AsyncRelayCommand(SaveFilterPresetAsync);

        SelectAllCommand           = new RelayCommand(SelectAll);
        DeselectAllCommand         = new RelayCommand(DeselectAll);
        ToggleBulkEditPanelCommand = new RelayCommand(() => IsBulkEditPanelOpen = !IsBulkEditPanelOpen);

        AddBulkTagCommand      = new RelayCommand(AddBulkTag);
        AddBulkPlayerCommand   = new RelayCommand(AddBulkPlayer);
        AddBulkGameCommand     = new RelayCommand(AddBulkGame);
        ClearBulkTagsCommand   = new RelayCommand(ClearBulkTags);
        ClearBulkPlayersCommand = new RelayCommand(ClearBulkPlayers);
        ClearBulkGameCommand   = new RelayCommand(ClearBulkGame);
        ApplyBulkEditsCommand  = new AsyncRelayCommand(ApplyBulkEditsAsync);
        CancelBulkEditsCommand = new RelayCommand(CancelBulkEdits);

        ShowBulkDeleteConfirmCommand        = new RelayCommand(() => IsBulkDeleteConfirmVisible = true);
        BulkTrashCommand                    = new AsyncRelayCommand(BulkTrashAsync);
        ShowRemoveAllBrokenConfirmCommand   = new RelayCommand(() => IsRemoveAllBrokenConfirmVisible = !IsRemoveAllBrokenConfirmVisible);
        ConfirmRemoveAllBrokenClipsCommand  = new AsyncRelayCommand(RemoveAllBrokenClipsAsync);
        BulkArchiveBrokenCommand            = new AsyncRelayCommand(BulkArchiveBrokenAsync);
        ClearStatusMessageCommand           = new RelayCommand(() => StatusMessage = null);

        ShowRemoveAllTagsConfirmCommand    = new RelayCommand(() => IsRemoveAllTagsConfirmVisible    = !IsRemoveAllTagsConfirmVisible);
        ConfirmRemoveAllTagsCommand        = new AsyncRelayCommand(RemoveAllTagsFromSelectedAsync);
        ShowRemoveAllPlayersConfirmCommand = new RelayCommand(() => IsRemoveAllPlayersConfirmVisible = !IsRemoveAllPlayersConfirmVisible);
        ConfirmRemoveAllPlayersCommand     = new AsyncRelayCommand(RemoveAllPlayersFromSelectedAsync);
        ShowRemoveAllGameConfirmCommand    = new RelayCommand(() => IsRemoveAllGameConfirmVisible    = !IsRemoveAllGameConfirmVisible);
        ConfirmRemoveAllGameCommand        = new AsyncRelayCommand(RemoveAllGameFromSelectedAsync);
        ShowBulkClearAllDataConfirmCommand = new RelayCommand(() => IsBulkClearAllDataConfirmVisible = !IsBulkClearAllDataConfirmVisible);
        ConfirmBulkClearAllDataCommand     = new AsyncRelayCommand(BulkClearAllDataAsync);

        BulkTranscribeCommand       = new AsyncRelayCommand(BulkTranscribeAsync, () => CanBulkTranscribe);
        CancelBulkTranscribeCommand = new RelayCommand(CancelBulkTranscribe, () => IsBulkTranscribing);

        StartCopyFormatCommand        = new RelayCommand(StartCopyFormat);
        ExitCopyFormatCommand         = new RelayCommand(ExitCopyFormat);
        PasteFormatToSelectionCommand = new AsyncRelayCommand(PasteFormatToSelectionAsync);

        IncreaseRowHeightCommand  = new RelayCommand(IncreaseRowHeight);
        DecreaseRowHeightCommand  = new RelayCommand(DecreaseRowHeight);
        ResetColumnWidthsCommand  = new RelayCommand(() => ColumnWidthsResetRequested?.Invoke());

        SelectedClips.CollectionChanged     += OnSelectedClipsChanged;
        FilterTagChips.CollectionChanged    += OnFilterChipsChanged;
        FilterGameChips.CollectionChanged   += OnFilterChipsChanged;
        FilterPlayerChips.CollectionChanged += OnFilterChipsChanged;
    }

    private void OnSelectedClipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Auto-open the panel when the first clip becomes selected.
        if (SelectedClips.Count > 0 && !IsBulkEditPanelOpen)
            IsBulkEditPanelOpen = true;

        OnPropertyChanged(nameof(HasSelectedClips));
        OnPropertyChanged(nameof(IsMultiSelectMode));
        OnPropertyChanged(nameof(IsCopyFormatButtonEnabled));
        OnPropertyChanged(nameof(SelectedClipsCountDisplay));
        OnPropertyChanged(nameof(HasSelectedBrokenClips));
        OnPropertyChanged(nameof(CanBulkTranscribe));
        BulkTranscribeCommand.NotifyCanExecuteChanged();

        RefreshBulkChipsFromSelection();
    }

    /// <summary>
    /// Requests that the given clip be opened in the detail/player view.
    /// In multi-select mode or copy-format mode, tapping a card toggles its selection instead of opening.
    /// In copy-format mode the source card cannot be selected.
    /// </summary>
    public void HandleCardTapped(ClipCardViewModel card)
    {
        // In copy-format mode: clicking any card (except source) selects/deselects it as a target.
        if (IsCopyFormatMode)
        {
            if (card.IsCopyFormatSource) return;
            OnClipSelectionChanged(card, !card.IsSelected);
            return;
        }

        if (IsMultiSelectMode)
        {
            OnClipSelectionChanged(card, !card.IsSelected);
            return;
        }

        // Broken clips cannot be opened — the source file is missing.
        if (card.IsBroken) return;

        // Record this clip as the last opened before navigating away.
        _lastOpenedClipId = card.ClipId;

        // Archived clips are not openable; exclude them from the navigation sequence.
        var sequence = Clips.Where(c => !c.IsArchived && !c.IsBroken).Select(c => c.ClipId).ToList();
        var index    = sequence.IndexOf(card.ClipId);
        ClipOpenRequested?.Invoke(card.ClipId, sequence, index);
    }

    /// <summary>
    /// Updates the selection state of a clip card and maintains <see cref="SelectedClips"/>.
    /// </summary>
    public void OnClipSelectionChanged(ClipCardViewModel card, bool selected)
    {
        if (card.IsSelected == selected) return;
        card.IsSelected = selected;

        if (selected)
            SelectedClips.Add(card);
        else
            SelectedClips.Remove(card);
    }

    /// <summary>
    /// Asynchronously loads all clips matching the current search and filter criteria.
    /// </summary>
    public async Task LoadAsync()
    {
        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        OnPropertyChanged(nameof(IsTranscriptionEnabled));
        DeselectAll();
        Clips.Clear();

        try
        {
            await LoadPickersAsync();
            if (token.IsCancellationRequested) return;

            var results = await _clipService.SearchAsync(BuildQuery());
            if (token.IsCancellationRequested) return;

            var sorted = ApplySort(results);

            foreach (var clip in sorted)
            {
                var card = new ClipCardViewModel(clip);
                card.DetailsRowHeight = DetailsRowHeight;
                card.IsLastVisited    = clip.Id == _lastOpenedClipId;

                // Wire quick-filter callbacks so clicking game/player in the details row adds a chip.
                card.QuickFilterGameRequested = id =>
                {
                    var name = AvailableGameTags.FirstOrDefault(t => t.Id == id)?.Name ?? "?";
                    AddFilterChip(FilterGameChips, id, name);
                    IsFilterPanelOpen = true;
                };
                card.QuickFilterPlayerRequested = id =>
                {
                    var name = AvailablePlayers.FirstOrDefault(p => p.Id == id)?.DisplayName ?? "?";
                    AddFilterChip(FilterPlayerChips, id, name);
                    IsFilterPanelOpen = true;
                };

                Clips.Add(card);
            }

            _ = Task.WhenAll(Clips.Select(c => c.LoadThumbnailAsync()));
            if (App.Services.GetRequiredService<ISettingsService>().Current.ShowImagesInLists)
                _ = Task.WhenAll(Clips.Select(c => c.LoadImagesAsync()));

            OnPropertyChanged(nameof(HasBrokenClips));
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    // ---- Partial property handlers ----

    partial void OnSearchTextChanged(string value) => LoadCommand.Execute(null);
    partial void OnSearchCaptionsChanged(bool value) => LoadCommand.Execute(null);

    partial void OnIsBulkTranscribingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanBulkTranscribe));
        BulkTranscribeCommand.NotifyCanExecuteChanged();
        CancelBulkTranscribeCommand.NotifyCanExecuteChanged();
    }
    partial void OnSortByChanged(string value) => LoadCommand.Execute(null);
    partial void OnFilterStatusChanged(ClipStatus? value)   { if (!_suppressFilterChanges) LoadCommand.Execute(null); }
    partial void OnFilterDateFromChanged(DateTime? value)   { if (!_suppressFilterChanges) LoadCommand.Execute(null); }
    partial void OnFilterDateToChanged(DateTime? value)     { if (!_suppressFilterChanges) LoadCommand.Execute(null); }
    partial void OnFilterIsFavouriteChanged(bool? value)    { if (!_suppressFilterChanges) LoadCommand.Execute(null); }

    partial void OnFilterGameTagChanged(Tag? value)
    {
        if (_suppressFilterChanges || value is null) return;
        AddFilterChip(FilterGameChips, value.Id, value.Name);
        FilterGameTag = null; // clear the picker after adding chip
    }

    partial void OnSelectedFilterTagChanged(Tag? value)
    {
        if (_suppressFilterChanges || value is null) return;
        AddFilterChip(FilterTagChips, value.Id, value.Name);
        SelectedFilterTag = null; // clear the picker after adding chip
    }

    partial void OnSelectedFilterPlayerChanged(Player? value)
    {
        if (_suppressFilterChanges || value is null) return;
        AddFilterChip(FilterPlayerChips, value.Id, value.DisplayName);
        SelectedFilterPlayer = null; // clear the picker after adding chip
    }

    private void OnFilterChipsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressFilterChanges)
            LoadCommand.Execute(null);
    }

    // ---- Multi-select helpers ----

    private void SelectAll()
    {
        foreach (var card in Clips)
            OnClipSelectionChanged(card, true);
    }

    private void DeselectAll()
    {
        foreach (var card in SelectedClips.ToList())
            card.IsSelected = false;

        SelectedClips.Clear();
        IsBulkEditPanelOpen              = false;
        IsBulkDeleteConfirmVisible       = false;
        IsRemoveAllTagsConfirmVisible    = false;
        IsRemoveAllPlayersConfirmVisible  = false;
        IsRemoveAllGameConfirmVisible     = false;
        IsBulkClearAllDataConfirmVisible  = false;
        CancelBulkEdits();
        ExitCopyFormat();
    }

    // ---- Bulk chip refresh ----

    /// <summary>
    /// Rebuilds all three chip collections (Tags, Players, Games) from the current selection.
    /// Excludes tags/players/game already optimistically removed via the remove buttons.
    /// </summary>
    private void RefreshBulkChipsFromSelection()
    {
        RefreshBulkTagsFromSelection();
        RefreshBulkPlayersFromSelection();
        RefreshBulkGameFromSelection();
    }

    /// <summary>Rebuilds <see cref="BulkPendingTags"/> from the current selection.</summary>
    private void RefreshBulkTagsFromSelection()
    {
        BulkPendingTags.Clear();
        if (SelectedClips.Count == 0) return;

        var tagSets = SelectedClips
            .Select(c => c.GeneralTagIds
                .Where(id => !_bulkRemovedTagIds.Contains(id))
                .ToHashSet())
            .ToList();

        var seenIds = new HashSet<int>();

        foreach (var tagId in tagSets.SelectMany(s => s).Distinct())
        {
            seenIds.Add(tagId);
            var tag = AvailableTags.FirstOrDefault(t => t.Id == tagId);
            if (tag is null) continue;

            if (_stagedNewTagIds.Contains(tagId))
            {
                BulkPendingTags.Add(new BulkTagChipViewModel(tagId, tag.Name, BulkTagStatus.New,
                    promote: null, remove: RemoveNewTagChip));
            }
            else
            {
                bool inAll = tagSets.All(s => s.Contains(tagId));
                var status = inAll ? BulkTagStatus.Shared : BulkTagStatus.Partial;
                BulkPendingTags.Add(new BulkTagChipViewModel(tagId, tag.Name, status,
                    promote: status == BulkTagStatus.Partial ? PromoteTagChip : null,
                    remove: id => RemoveExistingTagChip(id)));
            }
        }

        // Staged new tags not yet present in any selected clip.
        foreach (var tagId in _stagedNewTagIds)
        {
            if (seenIds.Contains(tagId)) continue;
            var tag = AvailableTags.FirstOrDefault(t => t.Id == tagId);
            if (tag is not null)
                BulkPendingTags.Add(new BulkTagChipViewModel(tagId, tag.Name, BulkTagStatus.New,
                    promote: null, remove: RemoveNewTagChip));
        }
    }

    /// <summary>Rebuilds <see cref="BulkPendingPlayers"/> from the current selection.</summary>
    private void RefreshBulkPlayersFromSelection()
    {
        BulkPendingPlayers.Clear();
        if (SelectedClips.Count == 0) return;

        var playerSets = SelectedClips
            .Select(c => c.PlayerTagIds
                .Where(id => !_bulkRemovedPlayerIds.Contains(id))
                .ToHashSet())
            .ToList();

        var seenIds = new HashSet<int>();

        foreach (var playerId in playerSets.SelectMany(s => s).Distinct())
        {
            seenIds.Add(playerId);
            var player = AvailablePlayers.FirstOrDefault(p => p.Id == playerId);
            if (player is null) continue;

            if (_stagedNewPlayerIds.Contains(playerId))
            {
                BulkPendingPlayers.Add(new BulkTagChipViewModel(playerId, player.DisplayName, BulkTagStatus.New,
                    promote: null, remove: RemoveNewPlayerChip));
            }
            else
            {
                bool inAll = playerSets.All(s => s.Contains(playerId));
                var status = inAll ? BulkTagStatus.Shared : BulkTagStatus.Partial;
                BulkPendingPlayers.Add(new BulkTagChipViewModel(playerId, player.DisplayName, status,
                    promote: status == BulkTagStatus.Partial ? PromotePlayerChip : null,
                    remove: id => RemoveExistingPlayerChip(id)));
            }
        }

        foreach (var playerId in _stagedNewPlayerIds)
        {
            if (seenIds.Contains(playerId)) continue;
            var player = AvailablePlayers.FirstOrDefault(p => p.Id == playerId);
            if (player is not null)
                BulkPendingPlayers.Add(new BulkTagChipViewModel(playerId, player.DisplayName, BulkTagStatus.New,
                    promote: null, remove: RemoveNewPlayerChip));
        }
    }

    /// <summary>Rebuilds <see cref="BulkPendingGames"/> from the current selection.</summary>
    private void RefreshBulkGameFromSelection()
    {
        BulkPendingGames.Clear();
        if (SelectedClips.Count == 0) return;

        // Group selected clips by their game tag ID (excluding the removed one).
        var gameGroups = SelectedClips
            .Where(c => c.GameTagId.HasValue && c.GameTagId.Value != _bulkRemovedGameTagId)
            .GroupBy(c => c.GameTagId!.Value)
            .ToList();

        var seenIds = new HashSet<int>();

        foreach (var group in gameGroups)
        {
            var gameTagId = group.Key;
            seenIds.Add(gameTagId);

            var gameTag = AvailableGameTags.FirstOrDefault(t => t.Id == gameTagId);
            if (gameTag is null) continue;

            if (_stagedNewGameId == gameTagId)
            {
                // User promoted this game to New.
                BulkPendingGames.Add(new BulkTagChipViewModel(gameTagId, gameTag.Name, BulkTagStatus.New,
                    promote: null, remove: RemoveNewGameChip));
            }
            else
            {
                bool inAll = group.Count() == SelectedClips.Count;
                var status = inAll ? BulkTagStatus.Shared : BulkTagStatus.Partial;
                BulkPendingGames.Add(new BulkTagChipViewModel(gameTagId, gameTag.Name, status,
                    promote: status == BulkTagStatus.Partial ? PromoteGameChip : null,
                    remove: id => RemoveExistingGameChip(id)));
            }
        }

        // Staged new game not in any selected clip.
        if (_stagedNewGameId.HasValue && !seenIds.Contains(_stagedNewGameId.Value))
        {
            var gameTag = AvailableGameTags.FirstOrDefault(t => t.Id == _stagedNewGameId);
            if (gameTag is not null)
                BulkPendingGames.Add(new BulkTagChipViewModel(_stagedNewGameId.Value, gameTag.Name, BulkTagStatus.New,
                    promote: null, remove: RemoveNewGameChip));
        }
    }

    // ---- Tag chip callbacks ----

    private void PromoteTagChip(BulkTagChipViewModel chip)
    {
        _stagedNewTagIds.Add(chip.EntityId);
        RefreshBulkTagsFromSelection();
    }

    private void RemoveNewTagChip(BulkTagChipViewModel chip)
    {
        _stagedNewTagIds.Remove(chip.EntityId);
        RefreshBulkTagsFromSelection();
    }

    private void RemoveExistingTagChip(BulkTagChipViewModel chip)
    {
        _bulkRemovedTagIds.Add(chip.EntityId);
        _stagedNewTagIds.Remove(chip.EntityId);
        RefreshBulkTagsFromSelection();
        _ = RemoveTagFromSelectedClipsAsync(chip.EntityId);
    }

    private async Task RemoveTagFromSelectedClipsAsync(int tagId)
    {
        var clipsWithTag = SelectedClips.Where(c => c.GeneralTagIds.Contains(tagId)).ToList();
        foreach (var card in clipsWithTag)
            await _clipService.RemoveTagAsync(card.ClipId, tagId);
    }

    // ---- Player chip callbacks ----

    private void PromotePlayerChip(BulkTagChipViewModel chip)
    {
        _stagedNewPlayerIds.Add(chip.EntityId);
        RefreshBulkPlayersFromSelection();
    }

    private void RemoveNewPlayerChip(BulkTagChipViewModel chip)
    {
        _stagedNewPlayerIds.Remove(chip.EntityId);
        RefreshBulkPlayersFromSelection();
    }

    private void RemoveExistingPlayerChip(BulkTagChipViewModel chip)
    {
        _bulkRemovedPlayerIds.Add(chip.EntityId);
        _stagedNewPlayerIds.Remove(chip.EntityId);
        RefreshBulkPlayersFromSelection();
        _ = RemovePlayerFromSelectedClipsAsync(chip.EntityId);
    }

    private async Task RemovePlayerFromSelectedClipsAsync(int playerId)
    {
        var clipsWithPlayer = SelectedClips.Where(c => c.PlayerTagIds.Contains(playerId)).ToList();
        foreach (var card in clipsWithPlayer)
            await _playerService.UntagClipAsync(card.ClipId, playerId);
    }

    // ---- Game chip callbacks ----

    private void PromoteGameChip(BulkTagChipViewModel chip)
    {
        _stagedNewGameId = chip.EntityId;
        RefreshBulkGameFromSelection();
    }

    private void RemoveNewGameChip(BulkTagChipViewModel chip)
    {
        if (_stagedNewGameId == chip.EntityId)
            _stagedNewGameId = null;
        RefreshBulkGameFromSelection();
    }

    private void RemoveExistingGameChip(BulkTagChipViewModel chip)
    {
        _bulkRemovedGameTagId = chip.EntityId;
        if (_stagedNewGameId == chip.EntityId)
            _stagedNewGameId = null;
        RefreshBulkGameFromSelection();
        _ = RemoveGameFromSelectedClipsAsync(chip.EntityId);
    }

    private async Task RemoveGameFromSelectedClipsAsync(int gameTagId)
    {
        var clipsWithGame = SelectedClips.Where(c => c.GameTagId == gameTagId).ToList();
        foreach (var card in clipsWithGame)
            await _clipService.RemoveTagAsync(card.ClipId, gameTagId);
    }

    // ---- Add/clear commands ----

    private void AddBulkTag()
    {
        if (BulkSelectedTag is null) return;
        _stagedNewTagIds.Add(BulkSelectedTag.Id);
        BulkSelectedTag = null;
        RefreshBulkTagsFromSelection();
    }

    private void AddBulkPlayer()
    {
        if (BulkSelectedPlayerPicker is null) return;
        _stagedNewPlayerIds.Add(BulkSelectedPlayerPicker.Id);
        BulkSelectedPlayerPicker = null;
        RefreshBulkPlayersFromSelection();
    }

    private void AddBulkGame()
    {
        if (BulkSelectedGamePicker is null) return;
        _stagedNewGameId = BulkSelectedGamePicker.Id;
        BulkSelectedGamePicker = null;
        RefreshBulkGameFromSelection();
    }

    private void ClearBulkTags()
    {
        _stagedNewTagIds.Clear();
        RefreshBulkTagsFromSelection();
    }

    private void ClearBulkPlayers()
    {
        _stagedNewPlayerIds.Clear();
        RefreshBulkPlayersFromSelection();
    }

    private void ClearBulkGame()
    {
        _stagedNewGameId = null;
        RefreshBulkGameFromSelection();
    }

    private void CancelBulkEdits()
    {
        _stagedNewTagIds.Clear();
        _stagedNewPlayerIds.Clear();
        _stagedNewGameId = null;
        _bulkRemovedTagIds.Clear();
        _bulkRemovedPlayerIds.Clear();
        _bulkRemovedGameTagId = null;
        RefreshBulkChipsFromSelection();
    }

    private async Task ApplyBulkEditsAsync()
    {
        if (SelectedClips.Count == 0) return;

        var ids = SelectedClips.Select(c => c.ClipId).ToList();

        if (BulkApplyTags)
            foreach (var chip in BulkPendingTags.Where(c => c.Status == BulkTagStatus.New).ToList())
                await _clipService.BulkAddTagAsync(ids, chip.EntityId);

        if (BulkApplyGame)
        {
            var newGame = BulkPendingGames.FirstOrDefault(c => c.Status == BulkTagStatus.New);
            if (newGame is not null)
                await _clipService.BulkSetGameAsync(ids, newGame.EntityId);
        }

        if (BulkApplyPlayers)
            foreach (var chip in BulkPendingPlayers.Where(c => c.Status == BulkTagStatus.New).ToList())
                foreach (var clipId in ids)
                    await _playerService.TagClipAsync(clipId, chip.EntityId);

        CancelBulkEdits();
        await LoadAsync();
    }

    private async Task BulkTrashAsync()
    {
        if (SelectedClips.Count == 0) return;
        var ids = SelectedClips.Select(c => c.ClipId).ToList();
        await _clipService.BulkTrashAsync(ids);
        DeselectAll();
        await LoadAsync();
    }

    private async Task RemoveAllBrokenClipsAsync()
    {
        var broken = Clips.Where(c => c.IsBroken).Select(c => c.ClipId).ToList();
        foreach (var id in broken)
            await _clipService.PermanentlyDeleteAsync(id);
        IsRemoveAllBrokenConfirmVisible = false;
        await LoadAsync();
    }

    private async Task BulkArchiveBrokenAsync()
    {
        var brokenSelected = SelectedClips.Where(c => c.IsBroken).Select(c => c.ClipId).ToList();
        foreach (var id in brokenSelected)
            await _clipService.SetStatusAsync(id, ClipStatus.Archived);
        DeselectAll();
        await LoadAsync();
    }

    // ---- Destructive bulk remove per category ----

    /// <summary>
    /// Removes ALL tags from all selected clips immediately, then reloads.
    /// This is a destructive operation and should only be called after user confirmation.
    /// </summary>
    private async Task RemoveAllTagsFromSelectedAsync()
    {
        IsRemoveAllTagsConfirmVisible = false;
        if (SelectedClips.Count == 0) return;
        foreach (var clip in SelectedClips.ToList())
            await _clipService.ClearTagsAsync(clip.ClipId);
        CancelBulkEdits();
        await LoadAsync();
    }

    /// <summary>
    /// Removes ALL players from all selected clips immediately, then reloads.
    /// This is a destructive operation and should only be called after user confirmation.
    /// </summary>
    private async Task RemoveAllPlayersFromSelectedAsync()
    {
        IsRemoveAllPlayersConfirmVisible = false;
        if (SelectedClips.Count == 0) return;
        foreach (var clip in SelectedClips.ToList())
            await _playerService.UntagAllAsync(clip.ClipId);
        CancelBulkEdits();
        await LoadAsync();
    }

    /// <summary>
    /// Removes the game tag from all selected clips immediately, then reloads.
    /// This is a destructive operation and should only be called after user confirmation.
    /// </summary>
    private async Task RemoveAllGameFromSelectedAsync()
    {
        IsRemoveAllGameConfirmVisible = false;
        if (SelectedClips.Count == 0) return;
        var ids = SelectedClips.Select(c => c.ClipId).ToList();
        await _clipService.BulkClearGameAsync(ids);
        CancelBulkEdits();
        await LoadAsync();
    }

    /// <summary>
    /// Clears all tags and players, resets the rating to zero, and sets the status to Unreviewed
    /// for all selected clips. This is a destructive operation called only after confirmation.
    /// </summary>
    private async Task BulkClearAllDataAsync()
    {
        IsBulkClearAllDataConfirmVisible = false;
        if (SelectedClips.Count == 0) return;

        var ids = SelectedClips.Select(c => c.ClipId).ToList();

        foreach (var id in ids)
        {
            await _clipService.ClearTagsAsync(id);
            await _playerService.UntagAllAsync(id);
            await _clipService.SetRatingAsync(id, 0);
            await _clipService.SetStatusAsync(id, ClipStudio.Core.Enums.ClipStatus.Unreviewed);
        }

        CancelBulkEdits();
        await LoadAsync();
    }

    // ---- Copy-format ----

    private void StartCopyFormat()
    {
        if (SelectedClips.Count != 1) return;

        _copyFormatSource               = SelectedClips[0];
        _copyFormatSource.IsCopyFormatSource = true;
        CopyFormatSourceDisplay         = _copyFormatSource.FileName;
        IsCopyFormatMode                = true;

        // Deselect the source so the user can select target clips.
        OnClipSelectionChanged(_copyFormatSource, false);

        BulkCopyPasteTags    = true;
        BulkCopyPastePlayers = true;
        BulkCopyPasteGame    = true;
    }

    private void ExitCopyFormat()
    {
        if (_copyFormatSource is not null)
            _copyFormatSource.IsCopyFormatSource = false;

        IsCopyFormatMode        = false;
        _copyFormatSource       = null;
        CopyFormatSourceDisplay = string.Empty;
    }

    private async Task PasteFormatToSelectionAsync()
    {
        if (_copyFormatSource is null || SelectedClips.Count == 0) return;

        var ids = SelectedClips.Select(c => c.ClipId).ToList();

        if (BulkCopyPasteTags)
            foreach (var tagId in _copyFormatSource.GeneralTagIds)
                await _clipService.BulkAddTagAsync(ids, tagId);

        if (BulkCopyPasteGame && _copyFormatSource.GameTagId.HasValue)
            await _clipService.BulkSetGameAsync(ids, _copyFormatSource.GameTagId.Value);

        if (BulkCopyPastePlayers)
            foreach (var playerId in _copyFormatSource.PlayerTagIds)
                foreach (var clipId in ids)
                    await _playerService.TagClipAsync(clipId, playerId);

        ExitCopyFormat();
        DeselectAll();
        await LoadAsync();
    }

    // ---- Private helpers ----

    private ClipSearchQuery BuildQuery()
    {
        var includedTagIds = FilterTagChips.Where(c => !c.IsExcluded).Select(c => c.Id)
            .Concat(FilterGameChips.Where(c => !c.IsExcluded).Select(c => c.Id))
            .ToList();

        var excludedTagIds = FilterTagChips.Where(c => c.IsExcluded).Select(c => c.Id)
            .Concat(FilterGameChips.Where(c => c.IsExcluded).Select(c => c.Id))
            .ToList();

        var includedPlayerIds = FilterPlayerChips.Where(c => !c.IsExcluded).Select(c => c.Id).ToList();
        var excludedPlayerIds = FilterPlayerChips.Where(c => c.IsExcluded).Select(c => c.Id).ToList();

        return new ClipSearchQuery
        {
            SearchText        = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            SearchCaptions    = SearchCaptions,
            Status            = FilterStatus,
            ExcludeArchived   = false,
            CreatedFrom       = FilterDateFrom.HasValue ? DateTime.SpecifyKind(FilterDateFrom.Value.Date, DateTimeKind.Local).ToUniversalTime() : null,
            CreatedTo         = FilterDateTo.HasValue ? DateTime.SpecifyKind(FilterDateTo.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime() : null,
            IsFavourite       = FilterIsFavourite,
            TagIds            = includedTagIds,
            PlayerIds         = includedPlayerIds,
            ExcludedTagIds    = excludedTagIds,
            ExcludedPlayerIds = excludedPlayerIds,
        };
    }

    private IEnumerable<Clip> ApplySort(IReadOnlyList<Clip> results)
    {
        return SortBy switch
        {
            "DateAsc"      => results.OrderBy(c => c.CreatedAt),
            "NameAsc"      => results.OrderBy(c => c.FileName),
            "NameDesc"     => results.OrderByDescending(c => c.FileName),
            "DurationDesc" => results.OrderByDescending(c => c.Duration),
            "DurationAsc"  => results.OrderBy(c => c.Duration),
            "RatingAsc"    => results.OrderBy(c => c.Rating),
            "RatingDesc"   => results.OrderByDescending(c => c.Rating),
            _              => results.OrderByDescending(c => c.CreatedAt),
        };
    }

    private void SetSort(string? column)
    {
        if (column is null) return;
        var ascKey  = column + "Asc";
        var descKey = column + "Desc";
        SortBy = SortBy == ascKey ? descKey : ascKey;
    }

    private async Task LoadPickersAsync()
    {
        _suppressFilterChanges = true;
        try
        {
            var generalTags = await _tagService.GetByTypeAsync(TagType.General);
            AvailableTags.Clear();
            foreach (var t in generalTags)
                AvailableTags.Add(t);

            var gameTags = await _tagService.GetByTypeAsync(TagType.Game);
            AvailableGameTags.Clear();
            foreach (var t in gameTags)
                AvailableGameTags.Add(t);

            var players = await _playerService.GetAllAsync();
            AvailablePlayers.Clear();
            foreach (var p in players)
                AvailablePlayers.Add(p);

            // Apply one-shot chips set by PresetFilters() (called from MainWindowViewModel navigation).
            var presetApplied = false;
            if (_presetGameTagId.HasValue && !FilterGameChips.Any(c => c.Id == _presetGameTagId.Value))
            {
                var gameTag = AvailableGameTags.FirstOrDefault(t => t.Id == _presetGameTagId.Value);
                if (gameTag is not null) { AddFilterChip(FilterGameChips, gameTag.Id, gameTag.Name); presetApplied = true; }
            }
            if (_presetTagId.HasValue && !FilterTagChips.Any(c => c.Id == _presetTagId.Value))
            {
                var tag = AvailableTags.FirstOrDefault(t => t.Id == _presetTagId.Value);
                if (tag is not null) { AddFilterChip(FilterTagChips, tag.Id, tag.Name); presetApplied = true; }
            }
            if (_presetPlayerId.HasValue && !FilterPlayerChips.Any(c => c.Id == _presetPlayerId.Value))
            {
                var player = AvailablePlayers.FirstOrDefault(p => p.Id == _presetPlayerId.Value);
                if (player is not null) { AddFilterChip(FilterPlayerChips, player.Id, player.DisplayName); presetApplied = true; }
            }
            if (presetApplied)
                IsFilterPanelOpen = true;

            _presetGameTagId = null;
            _presetTagId     = null;
            _presetPlayerId  = null;

            await LoadPresetsAsync();
        }
        finally
        {
            _suppressFilterChanges = false;
        }
    }

    private void ClearFilters()
    {
        FilterStatus         = null;
        FilterDateFrom       = null;
        FilterDateTo         = null;
        FilterGameTag        = null;
        FilterIsFavourite    = null;
        SelectedFilterTag    = null;
        SelectedFilterPlayer = null;
        SelectedTagIds.Clear();
        SelectedPlayerIds.Clear();
        FilterTagChips.Clear();
        FilterGameChips.Clear();
        FilterPlayerChips.Clear();
    }

    // ---- Filter chip helpers ----

    /// <summary>
    /// Adds a filter chip to <paramref name="collection"/> if one with the same <paramref name="id"/> does not
    /// already exist. The chip fires the <see cref="OnFilterChipsChanged"/> handler when removed
    /// or when its include/exclude state is toggled.
    /// </summary>
    private void AddFilterChip(ObservableCollection<FilterChipViewModel> collection, int id, string name, bool isExcluded = false)
    {
        if (collection.Any(c => c.Id == id)) return;
        var chip = new FilterChipViewModel(id, name,
            onRemove: c => collection.Remove(c),
            onChange: _ => { if (!_suppressFilterChanges) LoadCommand.Execute(null); });
        if (isExcluded) chip.IsExcluded = true;
        collection.Add(chip);
    }

    // ---- Filter preset persistence ----

    private async Task LoadPresetsAsync()
    {
        var presets = await _filterPresetService.GetAllAsync();
        SavedPresets.Clear();
        foreach (var p in presets)
        {
            var captured = p;
            SavedPresets.Add(new FilterPresetRowViewModel(
                captured.Id,
                captured.Name,
                onLoad: () => ApplyPreset(captured),
                onDeleteAsync: async () =>
                {
                    await _filterPresetService.DeleteAsync(captured.Id);
                    await LoadPresetsAsync();
                }));
        }
    }

    private void ApplyPreset(FilterPreset preset)
    {
        _suppressFilterChanges = true;
        try
        {
            ClearFilters();

            if (!string.IsNullOrEmpty(preset.Status) && Enum.TryParse<ClipStatus>(preset.Status, out var status))
                FilterStatus = status;
            FilterIsFavourite = preset.IsFavourite;

            foreach (var id in ParseIds(preset.IncludedTagIds))
            {
                var tag = AvailableTags.FirstOrDefault(t => t.Id == id);
                if (tag is not null)
                    AddFilterChip(FilterTagChips, tag.Id, tag.Name);
                else
                {
                    var gameTag = AvailableGameTags.FirstOrDefault(t => t.Id == id);
                    if (gameTag is not null) AddFilterChip(FilterGameChips, gameTag.Id, gameTag.Name);
                }
            }
            foreach (var id in ParseIds(preset.ExcludedTagIds))
            {
                var tag = AvailableTags.FirstOrDefault(t => t.Id == id);
                if (tag is not null)
                    AddFilterChip(FilterTagChips, tag.Id, tag.Name, isExcluded: true);
                else
                {
                    var gameTag = AvailableGameTags.FirstOrDefault(t => t.Id == id);
                    if (gameTag is not null) AddFilterChip(FilterGameChips, gameTag.Id, gameTag.Name, isExcluded: true);
                }
            }
            foreach (var id in ParseIds(preset.IncludedPlayerIds))
            {
                var player = AvailablePlayers.FirstOrDefault(p => p.Id == id);
                if (player is not null) AddFilterChip(FilterPlayerChips, player.Id, player.DisplayName);
            }
            foreach (var id in ParseIds(preset.ExcludedPlayerIds))
            {
                var player = AvailablePlayers.FirstOrDefault(p => p.Id == id);
                if (player is not null) AddFilterChip(FilterPlayerChips, player.Id, player.DisplayName, isExcluded: true);
            }

            IsFilterPanelOpen = true;
        }
        finally
        {
            _suppressFilterChanges = false;
            LoadCommand.Execute(null);
        }
    }

    private async Task SaveFilterPresetAsync()
    {
        if (string.IsNullOrWhiteSpace(SavePresetName)) return;

        var allIncludedTagIds = FilterTagChips.Where(c => !c.IsExcluded).Select(c => c.Id)
            .Concat(FilterGameChips.Where(c => !c.IsExcluded).Select(c => c.Id)).ToList();
        var allExcludedTagIds = FilterTagChips.Where(c => c.IsExcluded).Select(c => c.Id)
            .Concat(FilterGameChips.Where(c => c.IsExcluded).Select(c => c.Id)).ToList();
        var includedPlayerIds = FilterPlayerChips.Where(c => !c.IsExcluded).Select(c => c.Id).ToList();
        var excludedPlayerIds = FilterPlayerChips.Where(c => c.IsExcluded).Select(c => c.Id).ToList();

        var preset = new FilterPreset
        {
            Name              = SavePresetName.Trim(),
            IncludedTagIds    = allIncludedTagIds.Count > 0 ? string.Join(",", allIncludedTagIds) : null,
            ExcludedTagIds    = allExcludedTagIds.Count > 0 ? string.Join(",", allExcludedTagIds) : null,
            IncludedPlayerIds = includedPlayerIds.Count > 0 ? string.Join(",", includedPlayerIds) : null,
            ExcludedPlayerIds = excludedPlayerIds.Count > 0 ? string.Join(",", excludedPlayerIds) : null,
            Status            = FilterStatus?.ToString(),
            IsFavourite       = FilterIsFavourite,
        };

        await _filterPresetService.SaveAsync(preset);
        SavePresetName = string.Empty;
        await LoadPresetsAsync();
    }

    private static IReadOnlyList<int> ParseIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var id) ? id : -1)
            .Where(id => id > 0)
            .ToList();
    }

    // ---- Preset filters (called by MainWindowViewModel before navigating to Library) ----

    /// <summary>
    /// Stores filter IDs to be applied on the next <see cref="LoadAsync"/> call.
    /// Only one of the three parameters need be provided; the others remain unchanged.
    /// </summary>
    /// <param name="gameTagId">The ID of the game tag to pre-select in the filter panel, or null to leave unchanged.</param>
    /// <param name="tagId">The ID of the general tag to pre-select in the filter panel, or null to leave unchanged.</param>
    /// <param name="playerId">The ID of the player to pre-select in the filter panel, or null to leave unchanged.</param>
    public void PresetFilters(int? gameTagId = null, int? tagId = null, int? playerId = null)
    {
        _presetGameTagId = gameTagId;
        _presetTagId     = tagId;
        _presetPlayerId  = playerId;
    }

    // ---- Scroll state (read/written by LibraryView code-behind) ----

    /// <summary>Gets or sets the vertical scroll offset of the tiles / details ScrollViewer.
    /// Persisted across detail-view navigation so the user returns to the same position.</summary>
    public double TilesScrollOffsetY
    {
        get => _tilesScrollOffsetY;
        set => _tilesScrollOffsetY = value;
    }

    // ---- Row height helpers ----

    private void IncreaseRowHeight()
    {
        var newHeight = Math.Min(DetailsRowHeight + 8, 120);
        ApplyDetailsRowHeight(newHeight);
    }

    private void DecreaseRowHeight()
    {
        var newHeight = Math.Max(DetailsRowHeight - 8, 32);
        ApplyDetailsRowHeight(newHeight);
    }

    private void ApplyDetailsRowHeight(int height)
    {
        DetailsRowHeight = height;
        foreach (var card in Clips)
            card.DetailsRowHeight = height;

        var settings = App.Services.GetRequiredService<ISettingsService>();
        settings.Current.LibraryDetailsRowHeight = height;
        _ = settings.SaveAsync();
    }

    /// <summary>
    /// Initialises <see cref="DetailsRowHeight"/> from persisted settings.
    /// Called by <see cref="Views.LibraryView"/> once when attached to the visual tree.
    /// </summary>
    public void LoadRowHeightFromSettings()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        DetailsRowHeight = settings.Current.LibraryDetailsRowHeight;
    }

    /// <summary>
    /// Finds the card with the given clip identifier and updates its <see cref="ClipCardViewModel.IsBroken"/> flag.
    /// Called by <see cref="MainWindowViewModel"/> when the file-watcher fires a deletion event.
    /// </summary>
    /// <param name="clipId">The database identifier of the affected clip.</param>
    /// <param name="isBroken"><see langword="true"/> to mark broken; <see langword="false"/> to clear.</param>
    /// <summary>
    /// Notifies bindings that <see cref="HasBrokenClips"/> may have changed.
    /// Must be called on the UI thread.
    /// </summary>
    public void RefreshHasBrokenClips() => OnPropertyChanged(nameof(HasBrokenClips));

    public void UpdateClipBrokenState(int clipId, bool isBroken)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var card = Clips.FirstOrDefault(c => c.ClipId == clipId);
            if (card is null) return;
            card.IsBroken = isBroken;
            OnPropertyChanged(nameof(HasBrokenClips));
        });
    }

    // ---- Bulk transcription ----

    /// <summary>
    /// Transcribes all currently selected clips one by one using the Whisper model configured in Settings.
    /// Shows an inline status message when transcription is disabled or no model is available.
    /// </summary>
    private async Task BulkTranscribeAsync()
    {
        var settings = _settingsService.Current;

        if (!settings.TranscriptionEnabled)
        {
            BulkTranscribeStatus = "Transcription is disabled. Enable it in Settings > Transcription.";
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.TranscriptionModelPath)
            || !File.Exists(settings.TranscriptionModelPath))
        {
            BulkTranscribeStatus = "No model found. Configure one in Settings > Transcription.";
            return;
        }

        var clips = SelectedClips.ToList();
        if (clips.Count == 0) return;

        var trackIndices = ParseBulkTrackIndices(settings.TranscriptionAutoOnImportTrackIndices);

        _bulkTranscribeCts  = new CancellationTokenSource();
        IsBulkTranscribing  = true;
        BulkTranscribeProgress = 0f;
        BulkTranscribeStatus   = $"Starting — {clips.Count} clip{(clips.Count == 1 ? "" : "s")} queued...";

        var completed = 0;

        try
        {
            for (var i = 0; i < clips.Count; i++)
            {
                _bulkTranscribeCts.Token.ThrowIfCancellationRequested();

                var card = clips[i];
                BulkTranscribeStatus = $"{i + 1} / {clips.Count}: {card.FileName}";

                using var scope = _scopeFactory.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();

                try
                {
                    await svc.TranscribeAsync(
                        card.ClipId,
                        trackIndices,
                        settings.TranscriptionModelPath,
                        settings.TranscriptionBackend,
                        settings.TranscriptionLanguage,
                        progress: null,
                        cancellationToken: _bulkTranscribeCts.Token);

                    completed++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Skip failed clip and continue with the rest.
                }

                BulkTranscribeProgress = (float)(i + 1) / clips.Count;
            }

            BulkTranscribeStatus = completed == clips.Count
                ? $"Done — {completed} clip{(completed == 1 ? "" : "s")} transcribed."
                : $"Done — {completed} of {clips.Count} succeeded.";
        }
        catch (OperationCanceledException)
        {
            BulkTranscribeStatus = "Cancelled.";
        }
        finally
        {
            IsBulkTranscribing     = false;
            BulkTranscribeProgress = 0f;
            _bulkTranscribeCts?.Dispose();
            _bulkTranscribeCts = null;
        }
    }

    private void CancelBulkTranscribe() => _bulkTranscribeCts?.Cancel();

    /// <summary>
    /// Returns the paths of all currently registered source folders.
    /// Called by the view code-behind before opening the Relocate dialog.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetSourcePathsAsync()
    {
        using var scope   = _scopeFactory.CreateScope();
        var folderRepo    = scope.ServiceProvider.GetRequiredService<ISourceFolderRepository>();
        var folders       = await folderRepo.GetAllAsync();
        return folders.Select(f => f.Path).ToList();
    }

    /// <summary>
    /// Relocates the given clip to a new file path and optionally registers the new folder as a source.
    /// Called by the view code-behind after the Relocate dialog is confirmed.
    /// </summary>
    /// <param name="clipId">Database identifier of the clip to relocate.</param>
    /// <param name="newFilePath">Absolute path of the file in its new location.</param>
    /// <param name="addSourceFolderPath">
    /// When not <see langword="null"/>, the folder at this path is added as a watched source and
    /// the file watcher is started for it.
    /// </param>
    public async Task RelocateClipAsync(int clipId, string newFilePath, string? addSourceFolderPath)
    {
        using var scope    = _scopeFactory.CreateScope();
        var clipService    = scope.ServiceProvider.GetRequiredService<IClipService>();
        var folderRepo     = scope.ServiceProvider.GetRequiredService<ISourceFolderRepository>();
        var watcher        = scope.ServiceProvider.GetRequiredService<ILibraryWatcherService>();

        var allFolders     = await folderRepo.GetAllAsync();
        int? newSourceFolderId = null;

        if (!string.IsNullOrEmpty(addSourceFolderPath))
        {
            var normalised = Path.GetFullPath(addSourceFolderPath);
            var existing   = allFolders.FirstOrDefault(f =>
            {
                try { return string.Equals(Path.GetFullPath(f.Path), normalised, StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            });

            if (existing is not null)
            {
                newSourceFolderId = existing.Id;
            }
            else
            {
                var folder = new SourceFolder { Path = addSourceFolderPath, IsActive = true };
                await folderRepo.AddAsync(folder);
                watcher.StartWatching(folder.Path, folder.Id);
                newSourceFolderId = folder.Id;
            }
        }
        else
        {
            // MoveBack or file already in an existing source — resolve source by path prefix.
            newSourceFolderId = allFolders.FirstOrDefault(f =>
            {
                try { return newFilePath.StartsWith(Path.GetFullPath(f.Path), StringComparison.OrdinalIgnoreCase); }
                catch { return false; }
            })?.Id;
        }

        await clipService.RelocateAsync(clipId, newFilePath, newSourceFolderId);
        await LoadAsync();
    }

    /// <summary>
    /// Parses a comma-separated string of 0-based FFmpeg audio stream indices.
    /// Falls back to <c>[0]</c> when the string is empty or contains no valid entries.
    /// </summary>
    private static IReadOnlyList<int> ParseBulkTrackIndices(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [0];
        var result = csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var idx) ? idx : -1)
            .Where(idx => idx >= 0)
            .ToList();
        return result.Count > 0 ? result : [0];
    }
}
