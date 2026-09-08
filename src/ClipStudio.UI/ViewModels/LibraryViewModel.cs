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
using ClipStudio.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the main clip library view.
/// Displays all clips as a card grid or details list and exposes search, filter,
/// sort, view-mode toggle, clip-open, and staged bulk-edit capabilities.
/// </summary>
public sealed partial class LibraryViewModel : ViewModelBase, IBulkEditHost
{
    private readonly IClipService _clipService;
    private readonly ITagService _tagService;
    private readonly IPlayerService _playerService;
    private readonly IFilterPresetService _filterPresetService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settingsService;

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
    /// <summary>Gets a display string such as "3 clips selected".</summary>
    public string SelectedClipsCountDisplay =>
        $"{SelectedClips.Count} clip{(SelectedClips.Count == 1 ? "" : "s")} selected";

    /// <summary>Gets or sets a transient status or error message shown in the library toolbar.</summary>
    [ObservableProperty] private string? _statusMessage;

    /// <summary>Gets or sets a value indicating that the library is currently loading.</summary>
    [ObservableProperty] private bool _isLoading;

    /// <summary>Gets or sets the free-text search string used to filter displayed clips.</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the search should also look inside transcription
    /// segment text.  Disabled by default to avoid the extra DB query on every keystroke.
    /// </summary>
    [ObservableProperty] private bool _searchCaptions = false;

    /// <summary>
    /// Gets whether transcription is enabled in settings and the bulk transcribe button may appear.
    /// Refreshed on every <see cref="LoadAsync"/> call.
    /// </summary>
    public bool IsTranscriptionEnabled => _settingsService.Current.TranscriptionEnabled;

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

    /// <summary>
    /// Gets the editor for everything done to the selection at once: staged tags, players and
    /// game, copy-format, the destructive removals, trashing and bulk transcription.
    /// </summary>
    public BulkEditViewModel BulkEdit { get; }

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

    /// <summary>Gets the command that clears the transient status message from the toolbar.</summary>
    public IRelayCommand ClearStatusMessageCommand { get; }

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
        ClearStatusMessageCommand  = new RelayCommand(() => StatusMessage = null);

        BulkEdit = new BulkEditViewModel(this, clipService, playerService, scopeFactory, settingsService);

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
        OnPropertyChanged(nameof(HasSelectedClips));
        OnPropertyChanged(nameof(IsMultiSelectMode));
        OnPropertyChanged(nameof(SelectedClipsCountDisplay));
        OnPropertyChanged(nameof(HasSelectedBrokenClips));

        // The panel auto-open, the chip rebuild and the transcribe command's availability all
        // follow the selection, and all of them belong to the child.
        BulkEdit.NotifySelectionChanged();
    }

    /// <summary>
    /// Requests that the given clip be opened in the detail/player view.
    /// In multi-select mode or copy-format mode, tapping a card toggles its selection instead of opening.
    /// In copy-format mode the source card cannot be selected.
    /// </summary>
    public void HandleCardTapped(ClipCardViewModel card)
    {
        // In copy-format mode: clicking any card (except source) selects/deselects it as a target.
        if (BulkEdit.IsCopyFormatMode)
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
        BulkEdit.CloseConfirmations();
        BulkEdit.CancelEdits();
        BulkEdit.ExitCopyFormat();
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

    // ---- IBulkEditHost ----

    /// <inheritdoc />
    IReadOnlyList<ClipCardViewModel> IBulkEditHost.Clips => Clips;

    /// <inheritdoc />
    IReadOnlyList<ClipCardViewModel> IBulkEditHost.SelectedClips => SelectedClips;

    /// <inheritdoc />
    IReadOnlyList<Tag> IBulkEditHost.AvailableTags => AvailableTags;

    /// <inheritdoc />
    IReadOnlyList<Tag> IBulkEditHost.AvailableGameTags => AvailableGameTags;

    /// <inheritdoc />
    IReadOnlyList<Player> IBulkEditHost.AvailablePlayers => AvailablePlayers;

    /// <inheritdoc />
    Task IBulkEditHost.ReloadAsync() => LoadAsync();

    /// <inheritdoc />
    void IBulkEditHost.DeselectAll() => DeselectAll();

    /// <inheritdoc />
    void IBulkEditHost.SetClipSelected(ClipCardViewModel card, bool selected) =>
        OnClipSelectionChanged(card, selected);
}
