using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Players page.
/// Manages the list of players and provides a side-panel form for creating and editing player records,
/// including aliases and the "IsMe" flag that auto-tags new imports.
/// </summary>
public sealed partial class PlayersViewModel : ViewModelBase
{
    private readonly IPlayerService _playerService;

    // ---- Observable state ----

    /// <summary>Gets the full collection of player rows currently displayed.</summary>
    public ObservableCollection<PlayerRowViewModel> Players { get; } = new();

    /// <summary>Gets the aliases currently shown in the edit form for the selected player.</summary>
    public ObservableCollection<PlayerAlias> EditingAliases { get; } = new();

    /// <summary>
    /// Gets alias strings entered while creating a new (unsaved) player.
    /// These are persisted to the database once the player is saved.
    /// </summary>
    public ObservableCollection<string> PendingAliases { get; } = new();

    /// <summary>Gets or sets the filter string applied to the player list.</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>Gets or sets a value indicating whether a background load is running.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Gets or sets a value indicating whether the create / edit form is visible.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Gets or sets the player identifier being edited, or null when creating a new player.</summary>
    [ObservableProperty]
    private int? _editingPlayerId;

    /// <summary>Gets or sets the edit form title.</summary>
    [ObservableProperty]
    private string _editFormTitle = "Add Player";

    /// <summary>Gets or sets the display name field in the edit form.</summary>
    [ObservableProperty]
    private string _editName = string.Empty;

    /// <summary>Gets or sets whether the "IsMe" toggle is checked in the edit form.</summary>
    [ObservableProperty]
    private bool _editIsMe;

    /// <summary>Gets or sets the icon file path shown in the edit form.</summary>
    [ObservableProperty]
    private string _editIconPath = string.Empty;

    /// <summary>Gets or sets the alias text being typed in the add-alias field.</summary>
    [ObservableProperty]
    private string _editAliasText = string.Empty;

    /// <summary>Gets or sets a validation / error message from the last save or alias operation.</summary>
    [ObservableProperty]
    private string? _saveError;

    // ---- Commands ----

    /// <summary>Gets the command that loads all players from the database.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that opens the add-player form.</summary>
    public IRelayCommand BeginCreateCommand { get; }

    /// <summary>Gets the command that cancels the edit form without saving.</summary>
    public IRelayCommand CancelEditCommand { get; }

    /// <summary>Gets the command that saves the current edit form (create or update).</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>Gets the command that adds the alias currently entered in <see cref="EditAliasText"/> to the player.</summary>
    public IAsyncRelayCommand AddAliasCommand { get; }

    /// <summary>
    /// Gets the command that removes a pending alias (one entered before the new player has been saved).
    /// </summary>
    public IRelayCommand<string> RemovePendingAliasCommand { get; }

    /// <summary>
    /// Gets the command invoked when the icon path needs to be set from a file picker.
    /// The associated view code-behind calls <see cref="SetIconPath"/> after resolving the path.
    /// </summary>
    public IRelayCommand BrowseIconCommand { get; }

    /// <summary>Raised when the view should open a file picker to select a player icon.</summary>
    public event Action? BrowseIconRequested;

    /// <summary>
    /// Optional callback set by <see cref="MainWindowViewModel"/> to navigate to the Library
    /// page pre-filtered by a given player ID.
    /// </summary>
    public Action<int>? ViewInLibraryRequested { get; set; }

    /// <summary>
    /// Initialises a new <see cref="PlayersViewModel"/>.
    /// </summary>
    /// <param name="playerService">The application-layer player service.</param>
    public PlayersViewModel(IPlayerService playerService)
    {
        _playerService = playerService;

        LoadCommand              = new AsyncRelayCommand(LoadAsync);
        BeginCreateCommand       = new RelayCommand(BeginCreate);
        CancelEditCommand        = new RelayCommand(CancelEdit);
        SaveCommand              = new AsyncRelayCommand(SaveAsync);
        AddAliasCommand          = new AsyncRelayCommand(AddAliasAsync);
        RemovePendingAliasCommand = new RelayCommand<string>(a => { if (a is not null) PendingAliases.Remove(a); });
        BrowseIconCommand        = new RelayCommand(() => BrowseIconRequested?.Invoke());
    }

    // ---- Load ----

    /// <summary>
    /// Loads all players from the database, applies the current filter, and refreshes the displayed list.
    /// Icon bitmaps are loaded asynchronously after the list is populated.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        SaveError = null;

        try
        {
            var all    = await _playerService.GetAllAsync();
            var filter = FilterText.Trim();
            var filtered = string.IsNullOrEmpty(filter)
                ? all
                : all.Where(p => p.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                              || p.Aliases.Any(a => a.Alias.Contains(filter, StringComparison.OrdinalIgnoreCase)));

            Players.Clear();
            foreach (var player in filtered)
                Players.Add(new PlayerRowViewModel(player, BeginEdit, DeletePlayerAsync, ViewInLibraryRequested));

            _ = Task.WhenAll(Players.Select(r => r.LoadIconAsync()));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reloads the list when the filter text changes.</summary>
    partial void OnFilterTextChanged(string value) => LoadCommand.Execute(null);

    // ---- Set icon path (called from code-behind after file picker) ----

    /// <summary>
    /// Sets the icon path in the edit form. Called by the view code-behind after the user
    /// selects a file via the system file picker.
    /// </summary>
    /// <param name="path">The selected file path, or null to clear the current value.</param>
    public void SetIconPath(string? path)
        => EditIconPath = path ?? string.Empty;

    // ---- Create / Edit form ----

    private void BeginCreate()
    {
        EditingPlayerId = null;
        EditFormTitle   = "Add Player";
        EditName        = string.Empty;
        EditIsMe        = false;
        EditIconPath    = string.Empty;
        EditAliasText   = string.Empty;
        EditingAliases.Clear();
        PendingAliases.Clear();
        SaveError       = null;
        IsEditing       = true;
    }

    private void BeginEdit(PlayerRowViewModel row)
    {
        // We need the full entity to populate aliases; trigger an async load.
        EditingPlayerId = row.PlayerId;
        EditFormTitle   = "Edit Player";
        EditName        = row.Name;
        EditIsMe        = row.IsMe;
        EditIconPath    = row.IconPath ?? string.Empty;
        EditAliasText   = string.Empty;
        SaveError       = null;
        IsEditing       = true;

        _ = LoadEditingAliasesAsync(row.PlayerId);
    }

    private async Task LoadEditingAliasesAsync(int playerId)
    {
        var all = await _playerService.GetAllAsync();
        var player = all.FirstOrDefault(p => p.Id == playerId);
        EditingAliases.Clear();
        if (player is not null)
        {
            foreach (var alias in player.Aliases)
                EditingAliases.Add(alias);
        }
    }

    private void CancelEdit()
    {
        IsEditing = false;
        SaveError = null;
    }

    // ---- Save ----

    private async Task SaveAsync()
    {
        SaveError = null;

        var name = EditName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            SaveError = "Player name is required.";
            return;
        }

        try
        {
            if (EditingPlayerId.HasValue)
            {
                await _playerService.UpdateAsync(
                    EditingPlayerId.Value,
                    name,
                    EditIsMe,
                    string.IsNullOrWhiteSpace(EditIconPath) ? null : EditIconPath.Trim());
            }
            else
            {
                var created = await _playerService.CreateAsync(
                    name,
                    EditIsMe,
                    string.IsNullOrWhiteSpace(EditIconPath) ? null : EditIconPath.Trim());

                // Persist any aliases that were entered before the player was saved.
                foreach (var pendingAlias in PendingAliases.ToList())
                    await _playerService.AddAliasAsync(created.Id, pendingAlias);
                PendingAliases.Clear();
            }

            IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
    }

    // ---- Alias management ----

    private async Task AddAliasAsync()
    {
        var alias = EditAliasText.Trim();
        if (string.IsNullOrEmpty(alias))
            return;

        if (!EditingPlayerId.HasValue)
        {
            // New player not yet saved — buffer the alias locally.
            if (!PendingAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                PendingAliases.Add(alias);
            EditAliasText = string.Empty;
            return;
        }

        try
        {
            var newAlias = await _playerService.AddAliasAsync(EditingPlayerId.Value, alias);
            EditingAliases.Add(newAlias);
            EditAliasText = string.Empty;
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
    }

    /// <summary>
    /// Removes an alias from the player currently being edited.
    /// Called from the AXAML via a button command on each alias chip.
    /// </summary>
    /// <param name="alias">The alias entity to remove.</param>
    public async Task RemoveAliasAsync(PlayerAlias alias)
    {
        try
        {
            await _playerService.RemoveAliasAsync(alias.Id);
            EditingAliases.Remove(alias);
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
    }

    // ---- Delete ----

    private async Task DeletePlayerAsync(PlayerRowViewModel row)
    {
        try
        {
            await _playerService.DeleteAsync(row.PlayerId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
    }
}
