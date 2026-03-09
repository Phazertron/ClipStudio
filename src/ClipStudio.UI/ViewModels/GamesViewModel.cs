using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Games page (Game-type tags only).
/// Supports creating game tags via Steam search or manual entry, and editing / deleting existing entries.
/// Cover art is loaded asynchronously from the Steam CDN.
/// </summary>
public sealed partial class GamesViewModel : ViewModelBase
{
    private readonly ITagService _tagService;
    private readonly IGameSearchService _searchService;

    private static readonly HttpClient _http = new();

    // ---- Observable state ----

    /// <summary>Gets the full collection of Game tag rows currently displayed.</summary>
    public ObservableCollection<GameRowViewModel> Games { get; } = new();

    /// <summary>Gets the list of Steam search results shown in the selection list.</summary>
    public ObservableCollection<GameSearchResultViewModel> GameSearchResults { get; } = new();

    /// <summary>Gets or sets the filter string applied to the game list.</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>Gets or sets a value indicating whether a background load is running.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Gets or sets a value indicating whether the create / edit form is visible.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Gets or sets the tag identifier being edited, or null for a new game tag.</summary>
    [ObservableProperty]
    private int? _editingTagId;

    /// <summary>Gets or sets the game name in the edit form.</summary>
    [ObservableProperty]
    private string _editName = string.Empty;

    /// <summary>Gets or sets the edit form title (either "Add Game" or "Edit Game").</summary>
    [ObservableProperty]
    private string _editFormTitle = "Add Game";

    /// <summary>Gets or sets a validation / error message from the last save attempt.</summary>
    [ObservableProperty]
    private string? _saveError;

    /// <summary>Gets or sets the Steam search query string.</summary>
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Gets or sets a value indicating whether a Steam search is currently running.</summary>
    [ObservableProperty]
    private bool _isSearching;

    /// <summary>Gets or sets the currently selected Steam search result.</summary>
    [ObservableProperty]
    private GameSearchResultViewModel? _selectedGameSearchResult;

    // ---- Commands ----

    /// <summary>Gets the command that loads all Game tags from the database.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that opens the add-game form.</summary>
    public IRelayCommand BeginCreateCommand { get; }

    /// <summary>Gets the command that cancels the edit form without saving.</summary>
    public IRelayCommand CancelEditCommand { get; }

    /// <summary>Gets the command that saves the current edit form (create or update).</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>Gets the command that searches Steam for the current query string.</summary>
    public IAsyncRelayCommand SearchCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="GamesViewModel"/>.
    /// </summary>
    /// <param name="tagService">The application-layer tag service.</param>
    /// <param name="searchService">The game search service backed by the Steam Community API.</param>
    public GamesViewModel(ITagService tagService, IGameSearchService searchService)
    {
        _tagService    = tagService;
        _searchService = searchService;

        LoadCommand        = new AsyncRelayCommand(LoadAsync);
        BeginCreateCommand = new RelayCommand(BeginCreate);
        CancelEditCommand  = new RelayCommand(CancelEdit);
        SaveCommand        = new AsyncRelayCommand(SaveAsync);
        SearchCommand      = new AsyncRelayCommand(SearchAsync);
    }

    // ---- Load ----

    /// <summary>
    /// Loads all Game tags from the database and refreshes the displayed list.
    /// Cover art bitmaps are fetched asynchronously after the list is populated.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        SaveError = null;

        try
        {
            var all = await _tagService.GetAllAsync();
            Games.Clear();

            var filter   = FilterText.Trim();
            var filtered = string.IsNullOrEmpty(filter)
                ? all.Where(t => t.Type == Core.Enums.TagType.Game)
                : all.Where(t => t.Type == Core.Enums.TagType.Game
                              && t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

            foreach (var tag in filtered.OrderBy(t => t.Name))
                Games.Add(new GameRowViewModel(tag, BeginEdit, DeleteGameAsync));

            _ = Task.WhenAll(Games.Select(r => r.LoadCoverAsync(_http)));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reloads the list when the filter text changes.</summary>
    partial void OnFilterTextChanged(string value) => LoadCommand.Execute(null);

    // ---- Create / Edit form ----

    private void BeginCreate()
    {
        EditingTagId  = null;
        EditFormTitle = "Add Game";
        EditName      = string.Empty;
        SearchQuery   = string.Empty;
        ClearSearchResults();
        SaveError     = null;
        IsEditing     = true;
    }

    private void BeginEdit(GameRowViewModel row)
    {
        EditingTagId  = row.TagId;
        EditFormTitle = "Edit Game";
        EditName      = row.Name;
        SearchQuery   = string.Empty;
        ClearSearchResults();
        SaveError     = null;
        IsEditing     = true;
    }

    private void ClearSearchResults()
    {
        SelectedGameSearchResult = null;
        GameSearchResults.Clear();
    }

    private void SelectSearchResult(GameSearchResultViewModel result)
    {
        if (SelectedGameSearchResult == result)
        {
            // Toggle deselect: clicking the selected result again clears the selection.
            result.IsSelected        = false;
            SelectedGameSearchResult = null;
            EditName                 = string.Empty;
        }
        else
        {
            if (SelectedGameSearchResult is not null)
                SelectedGameSearchResult.IsSelected = false;

            result.IsSelected        = true;
            SelectedGameSearchResult = result;
            EditName                 = result.Game.Name;
        }
    }

    private void CancelEdit()
    {
        IsEditing = false;
        SaveError = null;
    }

    // ---- Steam search ----

    private async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        if (string.IsNullOrEmpty(query))
            return;

        IsSearching = true;
        ClearSearchResults();

        try
        {
            var results = await _searchService.SearchAsync(query);
            foreach (var game in results)
                GameSearchResults.Add(new GameSearchResultViewModel(game, SelectSearchResult));

            _ = Task.WhenAll(GameSearchResults.Select(r => r.LoadCoverAsync(_http)));
        }
        finally
        {
            IsSearching = false;
        }
    }

    // ---- Save ----

    private async Task SaveAsync()
    {
        SaveError = null;

        try
        {
            if (EditingTagId.HasValue)
            {
                // Editing an existing game tag — only rename is allowed.
                var name = EditName.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    SaveError = "Name is required.";
                    return;
                }

                var existing = await _tagService.GetByIdAsync(EditingTagId.Value);
                if (existing is not null)
                    await _tagService.UpdateAsync(EditingTagId.Value, name, existing.Color, existing.Description, existing.ParentTagId);
            }
            else if (SelectedGameSearchResult is not null)
            {
                // Duplicate check before creating from Steam result.
                var steamName = SelectedGameSearchResult.Game.Name;
                var allTags = await _tagService.GetAllAsync();
                if (allTags.Any(t => t.Type == Core.Enums.TagType.Game &&
                        string.Equals(t.Name, steamName, StringComparison.OrdinalIgnoreCase)))
                {
                    SaveError = $"A game named '{steamName}' already exists.";
                    return;
                }

                // Create from Steam search result — preserves cover art URL and app ID.
                await _tagService.CreateFromSteamAsync(SelectedGameSearchResult.Game);
            }
            else
            {
                // Create custom game tag with a manually entered name.
                var name = EditName.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    SaveError = "Enter a game name or select a Steam search result above.";
                    return;
                }

                // Duplicate check for manually entered names.
                var allTags2 = await _tagService.GetAllAsync();
                if (allTags2.Any(t => t.Type == Core.Enums.TagType.Game &&
                        string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    SaveError = $"A game named '{name}' already exists.";
                    return;
                }

                await _tagService.CreateCustomGameTagAsync(name);
            }

            IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
    }

    // ---- Cover art preload ----

    /// <summary>
    /// Downloads and caches cover art for all Steam-linked games in the background.
    /// Called once at application startup so images are available from disk when the
    /// user navigates to the Games page.
    /// </summary>
    public async Task PreloadCoversAsync()
    {
        try
        {
            var all = await _tagService.GetAllAsync();
            var steamGames = all
                .Where(t => t.Type == Core.Enums.TagType.Game && t.GameStoreAppId.HasValue && !string.IsNullOrEmpty(t.GameCoverUrl))
                .ToList();

            var rows = steamGames.Select(t => new GameRowViewModel(t, _ => { }, _ => Task.CompletedTask));
            await Task.WhenAll(rows.Select(r => r.LoadCoverAsync(_http)));
        }
        catch (Exception)
        {
            // Preload is best-effort; failures are silently ignored.
        }
    }

    // ---- Delete ----

    private async Task DeleteGameAsync(GameRowViewModel row)
    {
        try
        {
            await _tagService.DeleteAsync(row.TagId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
    }
}
