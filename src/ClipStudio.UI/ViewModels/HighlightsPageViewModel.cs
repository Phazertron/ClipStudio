using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Highlights page, which collects every highlight across all clips
/// and presents them as a searchable, sortable, filterable list. Each row opens the
/// parent clip constrained to the highlight's time range when clicked.
/// </summary>
public sealed partial class HighlightsPageViewModel : ViewModelBase
{
    private readonly IHighlightService _highlightService;
    private readonly ITagService _tagService;
    private readonly IPlayerService _playerService;

    private IReadOnlyList<HighlightRowViewModel> _allRows = Array.Empty<HighlightRowViewModel>();
    private CancellationTokenSource _loadCts = new();

    /// <summary>
    /// Callback wired by <see cref="MainWindowViewModel"/> so that clicking a highlight row
    /// opens the clip in watch mode with prev/next navigation support.
    /// Receives (clipId, startTime, endTime, filteredSequence, sequenceIndex).
    /// </summary>
    public Action<int, TimeSpan, TimeSpan, IReadOnlyList<HighlightRowViewModel>, int>? HighlightWatchRequested { get; set; }

    /// <summary>Gets the currently displayed (filtered + sorted) highlight rows.</summary>
    public ObservableCollection<HighlightRowViewModel> Highlights { get; } = new();

    // ---- Loading ----

    /// <summary>Gets or sets a value indicating whether a background load is running.</summary>
    [ObservableProperty] private bool _isLoading;

    // ---- Search ----

    /// <summary>Gets or sets the free-text search string used to filter highlights.</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    // ---- Sort ----

    /// <summary>
    /// Gets or sets the current sort key.
    /// Accepted values: "CreatedDesc", "CreatedAsc", "LabelAsc", "LabelDesc",
    /// "DurationDesc", "DurationAsc", "GameAsc", "GameDesc".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortIndicatorForLabel))]
    [NotifyPropertyChangedFor(nameof(SortIndicatorForDuration))]
    [NotifyPropertyChangedFor(nameof(SortIndicatorForGame))]
    [NotifyPropertyChangedFor(nameof(SortIndicatorForCreated))]
    private string _sortBy = "CreatedDesc";

    /// <summary>Gets a sort direction indicator for the Label column.</summary>
    public string SortIndicatorForLabel    => SortBy is "LabelAsc"    ? "▲" : SortBy is "LabelDesc"    ? "▼" : "";

    /// <summary>Gets a sort direction indicator for the Duration column.</summary>
    public string SortIndicatorForDuration => SortBy is "DurationAsc" ? "▲" : SortBy is "DurationDesc" ? "▼" : "";

    /// <summary>Gets a sort direction indicator for the Game column.</summary>
    public string SortIndicatorForGame     => SortBy is "GameAsc"     ? "▲" : SortBy is "GameDesc"     ? "▼" : "";

    /// <summary>Gets a sort direction indicator for the Created column.</summary>
    public string SortIndicatorForCreated  => SortBy is "CreatedAsc"  ? "▲" : SortBy is "CreatedDesc"  ? "▼" : "";

    // ---- Filter panel ----

    /// <summary>Gets or sets a value indicating whether the filter panel is visible.</summary>
    [ObservableProperty] private bool _isFilterPanelOpen;

    /// <summary>Gets or sets the game tag to filter highlights by. Null = all games.</summary>
    [ObservableProperty] private Tag? _filterGameTag;

    /// <summary>Gets or sets the general tag to filter highlights by. Null = all tags.</summary>
    [ObservableProperty] private Tag? _selectedFilterTag;

    /// <summary>Gets or sets the player to filter highlights by. Null = all players.</summary>
    [ObservableProperty] private Player? _selectedFilterPlayer;

    /// <summary>Gets or sets the earliest clip creation date filter (inclusive). Null = no lower bound.</summary>
    [ObservableProperty] private DateTime? _filterDateFrom;

    /// <summary>Gets or sets the latest clip creation date filter (inclusive). Null = no upper bound.</summary>
    [ObservableProperty] private DateTime? _filterDateTo;

    // ---- Picker data ----

    /// <summary>Gets the available general tags for the tag filter picker.</summary>
    public ObservableCollection<Tag> AvailableTags { get; } = new();

    /// <summary>Gets the available game tags for the game filter picker.</summary>
    public ObservableCollection<Tag> AvailableGameTags { get; } = new();

    /// <summary>Gets the available players for the player filter picker.</summary>
    public ObservableCollection<Player> AvailablePlayers { get; } = new();

    // ---- Commands ----

    /// <summary>Gets the command that loads or reloads highlights from the database.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that toggles the filter panel open or closed.</summary>
    public IRelayCommand ToggleFilterPanelCommand { get; }

    /// <summary>Gets the command that resets all filter fields to their defaults.</summary>
    public IRelayCommand ClearFiltersCommand { get; }

    /// <summary>Gets the command that sets the sort column (toggles asc/desc on repeated clicks).</summary>
    public IRelayCommand<string> SetSortCommand { get; }

    /// <summary>Gets the command that clears the game filter.</summary>
    public IRelayCommand ClearGameFilterCommand { get; }

    /// <summary>Gets the command that clears the tag filter.</summary>
    public IRelayCommand ClearTagFilterCommand { get; }

    /// <summary>Gets the command that clears the player filter.</summary>
    public IRelayCommand ClearPlayerFilterCommand { get; }

    /// <summary>Gets the command that clears the date-from filter.</summary>
    public IRelayCommand ClearDateFromCommand { get; }

    /// <summary>Gets the command that clears the date-to filter.</summary>
    public IRelayCommand ClearDateToCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="HighlightsPageViewModel"/>.
    /// </summary>
    /// <param name="highlightService">Service used to load all highlights.</param>
    /// <param name="tagService">Service used to populate tag filter pickers.</param>
    /// <param name="playerService">Service used to populate player filter picker.</param>
    public HighlightsPageViewModel(
        IHighlightService highlightService,
        ITagService tagService,
        IPlayerService playerService)
    {
        _highlightService = highlightService;
        _tagService       = tagService;
        _playerService    = playerService;

        LoadCommand              = new AsyncRelayCommand(LoadAsync);
        ToggleFilterPanelCommand = new RelayCommand(() => IsFilterPanelOpen = !IsFilterPanelOpen);
        ClearFiltersCommand      = new RelayCommand(ClearFilters);
        SetSortCommand           = new RelayCommand<string>(SetSort);
        ClearGameFilterCommand   = new RelayCommand(() => FilterGameTag       = null);
        ClearTagFilterCommand    = new RelayCommand(() => SelectedFilterTag   = null);
        ClearPlayerFilterCommand = new RelayCommand(() => SelectedFilterPlayer = null);
        ClearDateFromCommand     = new RelayCommand(() => FilterDateFrom      = null);
        ClearDateToCommand       = new RelayCommand(() => FilterDateTo        = null);
    }

    /// <summary>
    /// Loads all highlights from the database, populates filter pickers, and applies
    /// the current search/filter/sort state. Cancels any in-flight load.
    /// </summary>
    public async Task LoadAsync()
    {
        _loadCts.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        try
        {
            var (highlights, tags, gameTags, players) = await LoadDataAsync(ct);
            if (ct.IsCancellationRequested) return;

            _allRows = highlights.Select(h => new HighlightRowViewModel(h, OpenHighlightWatch)).ToList();

            RefreshPickers(tags, gameTags, players);
            ApplyFilterAndSort();

            // Load thumbnails without blocking UI.
            await Task.WhenAll(_allRows.Select(r => r.LoadThumbnailAsync()));
            if (App.Services.GetRequiredService<ISettingsService>().Current.ShowImagesInLists)
                _ = Task.WhenAll(_allRows.Select(r => r.LoadImagesAsync()));
        }
        catch (OperationCanceledException)
        {
            // Expected when a newer load cancels this one.
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsLoading = false;
        }
    }

    // ---- Property change reactions ----

    partial void OnSearchTextChanged(string value)           => ApplyFilterAndSort();
    partial void OnSortByChanged(string value)               => ApplyFilterAndSort();
    partial void OnFilterGameTagChanged(Tag? value)          => ApplyFilterAndSort();
    partial void OnSelectedFilterTagChanged(Tag? value)      => ApplyFilterAndSort();
    partial void OnSelectedFilterPlayerChanged(Player? value) => ApplyFilterAndSort();
    partial void OnFilterDateFromChanged(DateTime? value)    => ApplyFilterAndSort();
    partial void OnFilterDateToChanged(DateTime? value)      => ApplyFilterAndSort();

    // ---- Private helpers ----

    private async Task<(IReadOnlyList<Core.Entities.Highlight>, IReadOnlyList<Tag>, IReadOnlyList<Tag>, IReadOnlyList<Player>)>
        LoadDataAsync(CancellationToken ct)
    {
        var highlights = await _highlightService.GetAllAsync(ct);
        var tags       = await _tagService.GetByTypeAsync(TagType.General, ct);
        var gameTags   = await _tagService.GetByTypeAsync(TagType.Game, ct);
        var players    = await _playerService.GetAllAsync(ct);
        return (highlights, tags, gameTags, players);
    }

    private void RefreshPickers(
        IReadOnlyList<Tag> tags,
        IReadOnlyList<Tag> gameTags,
        IReadOnlyList<Player> players)
    {
        AvailableTags.Clear();
        foreach (var t in tags) AvailableTags.Add(t);

        AvailableGameTags.Clear();
        foreach (var g in gameTags) AvailableGameTags.Add(g);

        AvailablePlayers.Clear();
        foreach (var p in players) AvailablePlayers.Add(p);
    }

    private void ApplyFilterAndSort()
    {
        var filtered = ApplyFilters(_allRows);
        var sorted   = ApplySort(filtered);

        Highlights.Clear();
        foreach (var row in sorted)
            Highlights.Add(row);
    }

    private IEnumerable<HighlightRowViewModel> ApplyFilters(IEnumerable<HighlightRowViewModel> source)
    {
        var term = SearchText.Trim();

        foreach (var row in source)
        {
            // Text search
            if (!string.IsNullOrEmpty(term) &&
                !row.Label.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !row.ClipName.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !row.TagsDisplay.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !row.GameDisplay.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !row.PlayerDisplay.Contains(term, StringComparison.OrdinalIgnoreCase) &&
                !row.Notes.Contains(term, StringComparison.OrdinalIgnoreCase))
                continue;

            // Game filter
            if (FilterGameTag is not null && row.GameTagId != FilterGameTag.Id)
                continue;

            // Tag filter (highlights with this tag)
            if (SelectedFilterTag is not null &&
                !row.TagsDisplay.Split(',').Select(s => s.Trim())
                    .Contains(SelectedFilterTag.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            // Player filter (players on parent clip)
            if (SelectedFilterPlayer is not null &&
                !row.PlayerDisplay.Split(',').Select(s => s.Trim())
                    .Contains(SelectedFilterPlayer.DisplayName, StringComparer.OrdinalIgnoreCase))
                continue;

            // Date filters (based on highlight CreatedAt)
            if (FilterDateFrom.HasValue && row.CreatedAt.ToLocalTime().Date < FilterDateFrom.Value.Date)
                continue;
            if (FilterDateTo.HasValue && row.CreatedAt.ToLocalTime().Date > FilterDateTo.Value.Date)
                continue;

            yield return row;
        }
    }

    private IEnumerable<HighlightRowViewModel> ApplySort(IEnumerable<HighlightRowViewModel> source)
        => SortBy switch
        {
            "CreatedAsc"   => source.OrderBy(r => r.CreatedAt),
            "LabelAsc"     => source.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase),
            "LabelDesc"    => source.OrderByDescending(r => r.Label, StringComparer.OrdinalIgnoreCase),
            "DurationDesc" => source.OrderByDescending(r => r.Duration),
            "DurationAsc"  => source.OrderBy(r => r.Duration),
            "GameAsc"      => source.OrderBy(r => r.GameDisplay, StringComparer.OrdinalIgnoreCase),
            "GameDesc"     => source.OrderByDescending(r => r.GameDisplay, StringComparer.OrdinalIgnoreCase),
            _              => source.OrderByDescending(r => r.CreatedAt), // CreatedDesc default
        };

    private void SetSort(string? key)
    {
        if (key is null) return;

        // Toggle asc/desc when the same column is clicked again.
        SortBy = (key, SortBy) switch
        {
            ("Label",    "LabelAsc")     => "LabelDesc",
            ("Label",    _)              => "LabelAsc",
            ("Duration", "DurationDesc") => "DurationAsc",
            ("Duration", _)              => "DurationDesc",
            ("Game",     "GameAsc")      => "GameDesc",
            ("Game",     _)              => "GameAsc",
            ("Created",  "CreatedDesc")  => "CreatedAsc",
            ("Created",  _)              => "CreatedDesc",
            _                            => key,
        };
    }

    private void ClearFilters()
    {
        FilterGameTag        = null;
        SelectedFilterTag    = null;
        SelectedFilterPlayer = null;
        FilterDateFrom       = null;
        FilterDateTo         = null;
        SearchText           = string.Empty;
    }

    private void OpenHighlightWatch(HighlightRowViewModel row)
    {
        var idx = Highlights.IndexOf(row);
        HighlightWatchRequested?.Invoke(row.ClipId, row.StartTime, row.EndTime,
                                        (IReadOnlyList<HighlightRowViewModel>)Highlights, idx);
    }
}
