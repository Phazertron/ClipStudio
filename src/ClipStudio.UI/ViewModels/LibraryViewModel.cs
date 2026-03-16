using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
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

    // ---- Preset filter IDs applied on the next LoadAsync call ----
    private int? _presetGameTagId;
    private int? _presetTagId;
    private int? _presetPlayerId;

    // ---- Scroll + last-visited state (persists across detail-view navigation) ----
    private int _lastOpenedClipId;
    private double _tilesScrollOffsetY;

    /// <summary>Gets the observable collection of clip cards shown in the library.</summary>
    public ObservableCollection<ClipCardViewModel> Clips { get; } = new();

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
        IPlayerService playerService)
    {
        _clipService   = clipService;
        _tagService    = tagService;
        _playerService = playerService;

        LoadCommand                = new AsyncRelayCommand(LoadAsync);
        ToggleFilterPanelCommand   = new RelayCommand(() => IsFilterPanelOpen = !IsFilterPanelOpen);
        ClearFiltersCommand        = new RelayCommand(ClearFilters);
        ToggleViewModeCommand      = new RelayCommand(() => ViewMode = IsTilesView ? "Details" : "Tiles");
        ClearStatusFilterCommand   = new RelayCommand(() => FilterStatus     = null);
        ClearGameFilterCommand     = new RelayCommand(() => FilterGameTag    = null);
        ClearDateFromCommand       = new RelayCommand(() => FilterDateFrom   = null);
        ClearDateToCommand         = new RelayCommand(() => FilterDateTo     = null);
        ClearFavouriteFilterCommand = new RelayCommand(() => FilterIsFavourite  = null);
        ClearTagFilterCommand       = new RelayCommand(() => SelectedFilterTag    = null);
        ClearPlayerFilterCommand    = new RelayCommand(() => SelectedFilterPlayer = null);
        SetSortCommand              = new RelayCommand<string>(SetSort);

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

        ShowBulkDeleteConfirmCommand = new RelayCommand(() => IsBulkDeleteConfirmVisible = true);
        BulkTrashCommand             = new AsyncRelayCommand(BulkTrashAsync);

        ShowRemoveAllTagsConfirmCommand    = new RelayCommand(() => IsRemoveAllTagsConfirmVisible    = !IsRemoveAllTagsConfirmVisible);
        ConfirmRemoveAllTagsCommand        = new AsyncRelayCommand(RemoveAllTagsFromSelectedAsync);
        ShowRemoveAllPlayersConfirmCommand = new RelayCommand(() => IsRemoveAllPlayersConfirmVisible = !IsRemoveAllPlayersConfirmVisible);
        ConfirmRemoveAllPlayersCommand     = new AsyncRelayCommand(RemoveAllPlayersFromSelectedAsync);
        ShowRemoveAllGameConfirmCommand    = new RelayCommand(() => IsRemoveAllGameConfirmVisible    = !IsRemoveAllGameConfirmVisible);
        ConfirmRemoveAllGameCommand        = new AsyncRelayCommand(RemoveAllGameFromSelectedAsync);
        ShowBulkClearAllDataConfirmCommand = new RelayCommand(() => IsBulkClearAllDataConfirmVisible = !IsBulkClearAllDataConfirmVisible);
        ConfirmBulkClearAllDataCommand     = new AsyncRelayCommand(BulkClearAllDataAsync);

        StartCopyFormatCommand        = new RelayCommand(StartCopyFormat);
        ExitCopyFormatCommand         = new RelayCommand(ExitCopyFormat);
        PasteFormatToSelectionCommand = new AsyncRelayCommand(PasteFormatToSelectionAsync);

        IncreaseRowHeightCommand  = new RelayCommand(IncreaseRowHeight);
        DecreaseRowHeightCommand  = new RelayCommand(DecreaseRowHeight);
        ResetColumnWidthsCommand  = new RelayCommand(() => ColumnWidthsResetRequested?.Invoke());

        SelectedClips.CollectionChanged += OnSelectedClipsChanged;
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

        // Record this clip as the last opened before navigating away.
        _lastOpenedClipId = card.ClipId;

        // Archived clips are not openable; exclude them from the navigation sequence.
        var sequence = Clips.Where(c => !c.IsArchived).Select(c => c.ClipId).ToList();
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
                Clips.Add(card);
            }

            _ = Task.WhenAll(Clips.Select(c => c.LoadThumbnailAsync()));
            if (App.Services.GetRequiredService<ISettingsService>().Current.ShowImagesInLists)
                _ = Task.WhenAll(Clips.Select(c => c.LoadImagesAsync()));
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
    }

    // ---- Partial property handlers ----

    partial void OnSearchTextChanged(string value) => LoadCommand.Execute(null);
    partial void OnSortByChanged(string value) => LoadCommand.Execute(null);
    partial void OnFilterStatusChanged(ClipStatus? value)   { if (!_suppressFilterChanges) LoadCommand.Execute(null); }
    partial void OnFilterDateFromChanged(DateTime? value)   { if (!_suppressFilterChanges) LoadCommand.Execute(null); }
    partial void OnFilterDateToChanged(DateTime? value)     { if (!_suppressFilterChanges) LoadCommand.Execute(null); }
    partial void OnFilterGameTagChanged(Tag? value)         { if (!_suppressFilterChanges) LoadCommand.Execute(null); }
    partial void OnFilterIsFavouriteChanged(bool? value)    { if (!_suppressFilterChanges) LoadCommand.Execute(null); }
    partial void OnSelectedFilterTagChanged(Tag? value)     { if (!_suppressFilterChanges) LoadCommand.Execute(null); }
    partial void OnSelectedFilterPlayerChanged(Player? value) { if (!_suppressFilterChanges) LoadCommand.Execute(null); }

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
        var tagIds = SelectedTagIds.ToList();
        if (FilterGameTag is not null)
            tagIds.Add(FilterGameTag.Id);
        if (SelectedFilterTag is not null)
            tagIds.Add(SelectedFilterTag.Id);

        var playerIds = SelectedPlayerIds.ToList();
        if (SelectedFilterPlayer is not null)
            playerIds.Add(SelectedFilterPlayer.Id);

        return new ClipSearchQuery
        {
            SearchText      = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            Status          = FilterStatus,
            ExcludeArchived = false,
            CreatedFrom     = FilterDateFrom.HasValue ? DateTime.SpecifyKind(FilterDateFrom.Value.Date, DateTimeKind.Local).ToUniversalTime() : null,
            CreatedTo       = FilterDateTo.HasValue ? DateTime.SpecifyKind(FilterDateTo.Value.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime() : null,
            IsFavourite     = FilterIsFavourite,
            TagIds          = tagIds,
            PlayerIds       = playerIds,
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
        var savedGameTag      = FilterGameTag;
        var savedFilterTag    = SelectedFilterTag;
        var savedFilterPlayer = SelectedFilterPlayer;

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

            FilterGameTag        = savedGameTag      is not null ? AvailableGameTags.FirstOrDefault(t => t.Id == savedGameTag.Id)       : null;
            SelectedFilterTag    = savedFilterTag    is not null ? AvailableTags.FirstOrDefault(t => t.Id == savedFilterTag.Id)          : null;
            SelectedFilterPlayer = savedFilterPlayer is not null ? AvailablePlayers.FirstOrDefault(p => p.Id == savedFilterPlayer.Id)    : null;

            // Apply one-shot presets set by PresetFilters() (only overrides if currently unset).
            var presetApplied = false;
            if (_presetGameTagId.HasValue && FilterGameTag is null)
            {
                FilterGameTag   = AvailableGameTags.FirstOrDefault(t => t.Id == _presetGameTagId.Value);
                presetApplied   = FilterGameTag is not null;
            }
            if (_presetTagId.HasValue && SelectedFilterTag is null)
            {
                SelectedFilterTag = AvailableTags.FirstOrDefault(t => t.Id == _presetTagId.Value);
                presetApplied     = presetApplied || SelectedFilterTag is not null;
            }
            if (_presetPlayerId.HasValue && SelectedFilterPlayer is null)
            {
                SelectedFilterPlayer = AvailablePlayers.FirstOrDefault(p => p.Id == _presetPlayerId.Value);
                presetApplied        = presetApplied || SelectedFilterPlayer is not null;
            }
            if (presetApplied)
                IsFilterPanelOpen = true;

            _presetGameTagId = null;
            _presetTagId     = null;
            _presetPlayerId  = null;
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
}
