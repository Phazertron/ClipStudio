using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Everything the library does to a set of selected clips at once: staging tags, players and a game
/// for the whole selection, copying one clip's format onto others, the destructive bulk removals,
/// trashing, and bulk transcription.
/// </summary>
/// <remarks>
/// <para>
/// The model here is staging, not immediate application. Chips describe the selection rather than a
/// pending edit: a tag on every selected clip is <see cref="BulkTagStatus.Shared"/>, one on some of
/// them is <see cref="BulkTagStatus.Partial"/>, and only a chip the user explicitly added or
/// promoted is <see cref="BulkTagStatus.New"/>. Apply writes exactly the New chips, which is what
/// makes "promote" meaningful - it is how a partial tag is spread to the rest of the selection.
/// </para>
/// <para>
/// Removals are the exception and are applied immediately, because a chip cannot show "pending
/// removal" in that scheme. The removed id is tracked so the chip list stops showing it before the
/// reload lands.
/// </para>
/// </remarks>
public partial class BulkEditViewModel : ObservableObject
{
    private readonly IBulkEditHost _host;
    private readonly IClipService _clipService;
    private readonly IPlayerService _playerService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settingsService;

    // ---- Copy-format source ----
    private ClipCardViewModel? _copyFormatSource;

    // ---- Staged tags: IDs explicitly added or promoted by the user this session ----
    private readonly HashSet<int> _stagedNewTagIds    = new();
    private readonly HashSet<int> _stagedNewPlayerIds = new();
    private int? _stagedNewGameId;

    // ---- Optimistically removed IDs (immediate DB removal, hidden from the chip list) ----
    private readonly HashSet<int> _bulkRemovedTagIds    = new();
    private readonly HashSet<int> _bulkRemovedPlayerIds = new();
    private int? _bulkRemovedGameTagId;

    private CancellationTokenSource? _bulkTranscribeCts;

    /// <summary>Initialises a new <see cref="BulkEditViewModel"/>.</summary>
    /// <param name="host">The library view this editor acts on.</param>
    /// <param name="clipService">Clip tag, game, status and trash operations.</param>
    /// <param name="playerService">Player tagging operations.</param>
    /// <param name="scopeFactory">Creates the scope each transcription run resolves its service from.</param>
    /// <param name="settingsService">Supplies the transcription model, backend and language.</param>
    public BulkEditViewModel(
        IBulkEditHost host,
        IClipService clipService,
        IPlayerService playerService,
        IServiceScopeFactory scopeFactory,
        ISettingsService settingsService)
    {
        _host            = host;
        _clipService     = clipService;
        _playerService   = playerService;
        _scopeFactory    = scopeFactory;
        _settingsService = settingsService;

        TogglePanelCommand = new RelayCommand(() => IsPanelOpen = !IsPanelOpen);

        AddBulkTagCommand       = new RelayCommand(AddBulkTag);
        AddBulkPlayerCommand    = new RelayCommand(AddBulkPlayer);
        AddBulkGameCommand      = new RelayCommand(AddBulkGame);
        ClearBulkTagsCommand    = new RelayCommand(ClearBulkTags);
        ClearBulkPlayersCommand = new RelayCommand(ClearBulkPlayers);
        ClearBulkGameCommand    = new RelayCommand(ClearBulkGame);
        ApplyBulkEditsCommand   = new AsyncRelayCommand(ApplyBulkEditsAsync);
        CancelBulkEditsCommand  = new RelayCommand(CancelEdits);

        ShowBulkDeleteConfirmCommand       = new RelayCommand(() => IsBulkDeleteConfirmVisible = true);
        BulkTrashCommand                   = new AsyncRelayCommand(BulkTrashAsync);
        ShowRemoveAllBrokenConfirmCommand  = new RelayCommand(() => IsRemoveAllBrokenConfirmVisible = !IsRemoveAllBrokenConfirmVisible);
        ConfirmRemoveAllBrokenClipsCommand = new AsyncRelayCommand(RemoveAllBrokenClipsAsync);
        BulkArchiveBrokenCommand           = new AsyncRelayCommand(BulkArchiveBrokenAsync);

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
    }

    // ---- Panel and mode state ----

    /// <summary>
    /// Gets or sets whether the bulk edit panel is visible.
    /// Can be toggled from the toolbar regardless of selection state.
    /// Auto-opens when the first clip is selected.
    /// </summary>
    [ObservableProperty] private bool _isPanelOpen;

    /// <summary>
    /// Gets a value indicating whether the copy-format button should be enabled.
    /// True only when exactly one clip is selected and copy-format mode is not already active.
    /// </summary>
    public bool IsCopyFormatButtonEnabled => _host.SelectedClips.Count == 1 && !IsCopyFormatMode;

    /// <summary>Gets or sets a value indicating whether copy-format mode is active.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCopyFormatButtonEnabled))]
    private bool _isCopyFormatMode;

    /// <summary>Gets the name of the clip that was copied (used as a label in the paste UI).</summary>
    [ObservableProperty] private string _copyFormatSourceDisplay = string.Empty;

    // ---- Staged pending chips ----

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

    // ---- Pickers (for adding to staging) ----

    /// <summary>Gets or sets the tag selected in the bulk tag-add picker.</summary>
    [ObservableProperty] private Tag? _bulkSelectedTag;

    /// <summary>Gets or sets the player selected in the bulk player-add picker.</summary>
    [ObservableProperty] private Player? _bulkSelectedPlayerPicker;

    /// <summary>Gets or sets the game tag selected in the bulk game picker.</summary>
    [ObservableProperty] private Tag? _bulkSelectedGamePicker;

    // ---- Confirmation strips ----

    /// <summary>Gets or sets whether the bulk delete confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isBulkDeleteConfirmVisible;

    /// <summary>Gets or sets whether the "remove all broken clips" toolbar confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isRemoveAllBrokenConfirmVisible;

    /// <summary>Gets or sets whether the "remove all tags from selection" confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isRemoveAllTagsConfirmVisible;

    /// <summary>Gets or sets whether the "remove all players from selection" confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isRemoveAllPlayersConfirmVisible;

    /// <summary>Gets or sets whether the "remove game from selection" confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isRemoveAllGameConfirmVisible;

    /// <summary>Gets or sets whether the "clear all data from selection" confirmation strip is visible.</summary>
    [ObservableProperty] private bool _isBulkClearAllDataConfirmVisible;

    // ---- Bulk transcription ----

    /// <summary>Gets or sets whether a bulk transcription run is currently in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBulkTranscribe))]
    private bool _isBulkTranscribing;

    /// <summary>Gets or sets the bulk transcription progress (0-1).</summary>
    [ObservableProperty] private float _bulkTranscribeProgress;

    /// <summary>Gets or sets the status message shown in the bulk transcription section.</summary>
    [ObservableProperty] private string _bulkTranscribeStatus = string.Empty;

    /// <summary>Gets whether the bulk transcribe command can execute.</summary>
    public bool CanBulkTranscribe => _host.SelectedClips.Count > 0 && !IsBulkTranscribing;

    // ---- Commands ----

    /// <summary>Gets the command that shows or hides the bulk edit panel.</summary>
    public IRelayCommand TogglePanelCommand { get; }

    /// <summary>Gets the command that stages the tag chosen in the bulk tag picker.</summary>
    public IRelayCommand AddBulkTagCommand { get; }

    /// <summary>Gets the command that stages the player chosen in the bulk player picker.</summary>
    public IRelayCommand AddBulkPlayerCommand { get; }

    /// <summary>Gets the command that stages the game chosen in the bulk game picker.</summary>
    public IRelayCommand AddBulkGameCommand { get; }

    /// <summary>Gets the command that discards all staged tag chips.</summary>
    public IRelayCommand ClearBulkTagsCommand { get; }

    /// <summary>Gets the command that discards all staged player chips.</summary>
    public IRelayCommand ClearBulkPlayersCommand { get; }

    /// <summary>Gets the command that discards the staged game chip.</summary>
    public IRelayCommand ClearBulkGameCommand { get; }

    /// <summary>Gets the command that writes every staged New chip to the whole selection.</summary>
    public IAsyncRelayCommand ApplyBulkEditsCommand { get; }

    /// <summary>Gets the command that discards all staging without writing anything.</summary>
    public IRelayCommand CancelBulkEditsCommand { get; }

    /// <summary>Gets the command that reveals the bulk trash confirmation strip.</summary>
    public IRelayCommand ShowBulkDeleteConfirmCommand { get; }

    /// <summary>Gets the command that moves every selected clip to the trash.</summary>
    public IAsyncRelayCommand BulkTrashCommand { get; }

    /// <summary>Gets the command that toggles the "remove all broken clips" confirmation strip.</summary>
    public IRelayCommand ShowRemoveAllBrokenConfirmCommand { get; }

    /// <summary>Gets the command that permanently deletes every broken clip in the current view.</summary>
    public IAsyncRelayCommand ConfirmRemoveAllBrokenClipsCommand { get; }

    /// <summary>Gets the command that archives the broken clips within the selection.</summary>
    public IAsyncRelayCommand BulkArchiveBrokenCommand { get; }

    /// <summary>Gets the command that toggles the "remove all tags" confirmation strip.</summary>
    public IRelayCommand ShowRemoveAllTagsConfirmCommand { get; }

    /// <summary>Gets the command that removes every tag from every selected clip.</summary>
    public IAsyncRelayCommand ConfirmRemoveAllTagsCommand { get; }

    /// <summary>Gets the command that toggles the "remove all players" confirmation strip.</summary>
    public IRelayCommand ShowRemoveAllPlayersConfirmCommand { get; }

    /// <summary>Gets the command that removes every player from every selected clip.</summary>
    public IAsyncRelayCommand ConfirmRemoveAllPlayersCommand { get; }

    /// <summary>Gets the command that toggles the "remove game" confirmation strip.</summary>
    public IRelayCommand ShowRemoveAllGameConfirmCommand { get; }

    /// <summary>Gets the command that clears the game tag on every selected clip.</summary>
    public IAsyncRelayCommand ConfirmRemoveAllGameCommand { get; }

    /// <summary>Gets the command that toggles the "clear all data" confirmation strip.</summary>
    public IRelayCommand ShowBulkClearAllDataConfirmCommand { get; }

    /// <summary>Gets the command that clears tags, players, rating and status on the selection.</summary>
    public IAsyncRelayCommand ConfirmBulkClearAllDataCommand { get; }

    /// <summary>Gets the command that transcribes every selected clip in turn.</summary>
    public IAsyncRelayCommand BulkTranscribeCommand { get; }

    /// <summary>Gets the command that cancels an in-progress bulk transcription run.</summary>
    public IRelayCommand CancelBulkTranscribeCommand { get; }

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

    // ---- Selection notifications from the host ----

    /// <summary>
    /// Called by the host whenever the selection changes, so the chips, the derived flags and the
    /// transcribe command's availability all follow it.
    /// </summary>
    public virtual void NotifySelectionChanged()
    {
        if (_host.SelectedClips.Count > 0 && !IsPanelOpen)
            IsPanelOpen = true;

        OnPropertyChanged(nameof(IsCopyFormatButtonEnabled));
        OnPropertyChanged(nameof(CanBulkTranscribe));
        BulkTranscribeCommand.NotifyCanExecuteChanged();

        RefreshChipsFromSelection();
    }

    /// <summary>
    /// Closes every confirmation strip. Called by the host when the selection is cleared, so a
    /// confirmation cannot survive the selection it referred to.
    /// </summary>
    public virtual void CloseConfirmations()
    {
        IsPanelOpen                      = false;
        IsBulkDeleteConfirmVisible       = false;
        IsRemoveAllTagsConfirmVisible    = false;
        IsRemoveAllPlayersConfirmVisible = false;
        IsRemoveAllGameConfirmVisible    = false;
        IsBulkClearAllDataConfirmVisible = false;
    }

    // ---- Chip refresh ----

    /// <summary>
    /// Rebuilds all three chip collections (Tags, Players, Games) from the current selection.
    /// Excludes tags/players/game already optimistically removed via the remove buttons.
    /// </summary>
    public void RefreshChipsFromSelection()
    {
        RefreshBulkTagsFromSelection();
        RefreshBulkPlayersFromSelection();
        RefreshBulkGameFromSelection();
    }

    /// <summary>Rebuilds <see cref="BulkPendingTags"/> from the current selection.</summary>
    private void RefreshBulkTagsFromSelection()
    {
        BulkPendingTags.Clear();
        if (_host.SelectedClips.Count == 0) return;

        var tagSets = _host.SelectedClips
            .Select(c => c.GeneralTagIds
                .Where(id => !_bulkRemovedTagIds.Contains(id))
                .ToHashSet())
            .ToList();

        var seenIds = new HashSet<int>();

        foreach (var tagId in tagSets.SelectMany(s => s).Distinct())
        {
            seenIds.Add(tagId);
            var tag = _host.AvailableTags.FirstOrDefault(t => t.Id == tagId);
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
            var tag = _host.AvailableTags.FirstOrDefault(t => t.Id == tagId);
            if (tag is not null)
                BulkPendingTags.Add(new BulkTagChipViewModel(tagId, tag.Name, BulkTagStatus.New,
                    promote: null, remove: RemoveNewTagChip));
        }
    }

    /// <summary>Rebuilds <see cref="BulkPendingPlayers"/> from the current selection.</summary>
    private void RefreshBulkPlayersFromSelection()
    {
        BulkPendingPlayers.Clear();
        if (_host.SelectedClips.Count == 0) return;

        var playerSets = _host.SelectedClips
            .Select(c => c.PlayerTagIds
                .Where(id => !_bulkRemovedPlayerIds.Contains(id))
                .ToHashSet())
            .ToList();

        var seenIds = new HashSet<int>();

        foreach (var playerId in playerSets.SelectMany(s => s).Distinct())
        {
            seenIds.Add(playerId);
            var player = _host.AvailablePlayers.FirstOrDefault(p => p.Id == playerId);
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
            var player = _host.AvailablePlayers.FirstOrDefault(p => p.Id == playerId);
            if (player is not null)
                BulkPendingPlayers.Add(new BulkTagChipViewModel(playerId, player.DisplayName, BulkTagStatus.New,
                    promote: null, remove: RemoveNewPlayerChip));
        }
    }

    /// <summary>Rebuilds <see cref="BulkPendingGames"/> from the current selection.</summary>
    private void RefreshBulkGameFromSelection()
    {
        BulkPendingGames.Clear();
        if (_host.SelectedClips.Count == 0) return;

        // Group selected clips by their game tag ID (excluding the removed one).
        var gameGroups = _host.SelectedClips
            .Where(c => c.GameTagId.HasValue && c.GameTagId.Value != _bulkRemovedGameTagId)
            .GroupBy(c => c.GameTagId!.Value)
            .ToList();

        var seenIds = new HashSet<int>();

        foreach (var group in gameGroups)
        {
            var gameTagId = group.Key;
            seenIds.Add(gameTagId);

            var gameTag = _host.AvailableGameTags.FirstOrDefault(t => t.Id == gameTagId);
            if (gameTag is null) continue;

            if (_stagedNewGameId == gameTagId)
            {
                // User promoted this game to New.
                BulkPendingGames.Add(new BulkTagChipViewModel(gameTagId, gameTag.Name, BulkTagStatus.New,
                    promote: null, remove: RemoveNewGameChip));
            }
            else
            {
                bool inAll = group.Count() == _host.SelectedClips.Count;
                var status = inAll ? BulkTagStatus.Shared : BulkTagStatus.Partial;
                BulkPendingGames.Add(new BulkTagChipViewModel(gameTagId, gameTag.Name, status,
                    promote: status == BulkTagStatus.Partial ? PromoteGameChip : null,
                    remove: id => RemoveExistingGameChip(id)));
            }
        }

        // Staged new game not in any selected clip.
        if (_stagedNewGameId.HasValue && !seenIds.Contains(_stagedNewGameId.Value))
        {
            var gameTag = _host.AvailableGameTags.FirstOrDefault(t => t.Id == _stagedNewGameId);
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
        var clipsWithTag = _host.SelectedClips.Where(c => c.GeneralTagIds.Contains(tagId)).ToList();
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
        var clipsWithPlayer = _host.SelectedClips.Where(c => c.PlayerTagIds.Contains(playerId)).ToList();
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
        var clipsWithGame = _host.SelectedClips.Where(c => c.GameTagId == gameTagId).ToList();
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

    /// <summary>Discards every staged addition and every optimistic removal, then rebuilds the chips.</summary>
    public void CancelEdits()
    {
        _stagedNewTagIds.Clear();
        _stagedNewPlayerIds.Clear();
        _stagedNewGameId = null;
        _bulkRemovedTagIds.Clear();
        _bulkRemovedPlayerIds.Clear();
        _bulkRemovedGameTagId = null;
        RefreshChipsFromSelection();
    }

    private async Task ApplyBulkEditsAsync()
    {
        if (_host.SelectedClips.Count == 0) return;

        var ids = _host.SelectedClips.Select(c => c.ClipId).ToList();

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

        CancelEdits();
        await _host.ReloadAsync();
    }

    private async Task BulkTrashAsync()
    {
        if (_host.SelectedClips.Count == 0) return;
        var ids = _host.SelectedClips.Select(c => c.ClipId).ToList();
        await _clipService.BulkTrashAsync(ids);
        _host.DeselectAll();
        await _host.ReloadAsync();
    }

    private async Task RemoveAllBrokenClipsAsync()
    {
        var broken = _host.Clips.Where(c => c.IsBroken).Select(c => c.ClipId).ToList();
        foreach (var id in broken)
            await _clipService.PermanentlyDeleteAsync(id);
        IsRemoveAllBrokenConfirmVisible = false;
        await _host.ReloadAsync();
    }

    private async Task BulkArchiveBrokenAsync()
    {
        var brokenSelected = _host.SelectedClips.Where(c => c.IsBroken).Select(c => c.ClipId).ToList();
        foreach (var id in brokenSelected)
            await _clipService.SetStatusAsync(id, ClipStatus.Archived);
        _host.DeselectAll();
        await _host.ReloadAsync();
    }

    // ---- Destructive bulk remove per category ----

    /// <summary>
    /// Removes ALL tags from all selected clips immediately, then reloads.
    /// This is a destructive operation and should only be called after user confirmation.
    /// </summary>
    private async Task RemoveAllTagsFromSelectedAsync()
    {
        IsRemoveAllTagsConfirmVisible = false;
        if (_host.SelectedClips.Count == 0) return;
        foreach (var clip in _host.SelectedClips.ToList())
            await _clipService.ClearTagsAsync(clip.ClipId);
        CancelEdits();
        await _host.ReloadAsync();
    }

    /// <summary>
    /// Removes ALL players from all selected clips immediately, then reloads.
    /// This is a destructive operation and should only be called after user confirmation.
    /// </summary>
    private async Task RemoveAllPlayersFromSelectedAsync()
    {
        IsRemoveAllPlayersConfirmVisible = false;
        if (_host.SelectedClips.Count == 0) return;
        foreach (var clip in _host.SelectedClips.ToList())
            await _playerService.UntagAllAsync(clip.ClipId);
        CancelEdits();
        await _host.ReloadAsync();
    }

    /// <summary>
    /// Removes the game tag from all selected clips immediately, then reloads.
    /// This is a destructive operation and should only be called after user confirmation.
    /// </summary>
    private async Task RemoveAllGameFromSelectedAsync()
    {
        IsRemoveAllGameConfirmVisible = false;
        if (_host.SelectedClips.Count == 0) return;
        var ids = _host.SelectedClips.Select(c => c.ClipId).ToList();
        await _clipService.BulkClearGameAsync(ids);
        CancelEdits();
        await _host.ReloadAsync();
    }

    /// <summary>
    /// Clears all tags and players, resets the rating to zero, and sets the status to Unreviewed
    /// for all selected clips. This is a destructive operation called only after confirmation.
    /// </summary>
    private async Task BulkClearAllDataAsync()
    {
        IsBulkClearAllDataConfirmVisible = false;
        if (_host.SelectedClips.Count == 0) return;

        var ids = _host.SelectedClips.Select(c => c.ClipId).ToList();

        foreach (var id in ids)
        {
            await _clipService.ClearTagsAsync(id);
            await _playerService.UntagAllAsync(id);
            await _clipService.SetRatingAsync(id, 0);
            await _clipService.SetStatusAsync(id, ClipStatus.Unreviewed);
        }

        CancelEdits();
        await _host.ReloadAsync();
    }

    // ---- Copy-format ----

    private void StartCopyFormat()
    {
        if (_host.SelectedClips.Count != 1) return;

        _copyFormatSource                    = _host.SelectedClips[0];
        _copyFormatSource.IsCopyFormatSource = true;
        CopyFormatSourceDisplay              = _copyFormatSource.FileName;
        IsCopyFormatMode                     = true;

        // Deselect the source so the user can select target clips.
        _host.SetClipSelected(_copyFormatSource, false);

        BulkCopyPasteTags    = true;
        BulkCopyPastePlayers = true;
        BulkCopyPasteGame    = true;
    }

    /// <summary>Leaves copy-format mode and clears the source highlight, applying nothing.</summary>
    public void ExitCopyFormat()
    {
        if (_copyFormatSource is not null)
            _copyFormatSource.IsCopyFormatSource = false;

        IsCopyFormatMode        = false;
        _copyFormatSource       = null;
        CopyFormatSourceDisplay = string.Empty;
    }

    private async Task PasteFormatToSelectionAsync()
    {
        if (_copyFormatSource is null || _host.SelectedClips.Count == 0) return;

        var ids = _host.SelectedClips.Select(c => c.ClipId).ToList();

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
        _host.DeselectAll();
        await _host.ReloadAsync();
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

        var clips = _host.SelectedClips.ToList();
        if (clips.Count == 0) return;

        var trackIndices = ParseBulkTrackIndices(settings.TranscriptionAutoOnImportTrackIndices);

        _bulkTranscribeCts     = new CancellationTokenSource();
        IsBulkTranscribing     = true;
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

    partial void OnIsBulkTranscribingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanBulkTranscribe));
        BulkTranscribeCommand.NotifyCanExecuteChanged();
        CancelBulkTranscribeCommand.NotifyCanExecuteChanged();
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
