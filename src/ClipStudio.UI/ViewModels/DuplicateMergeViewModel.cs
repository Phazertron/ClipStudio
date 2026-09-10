using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Resolves one group of duplicate clips: which copy survives, what metadata it ends up with, and
/// what happens to the others.
/// </summary>
/// <remarks>
/// The files are byte-identical, so nothing about the recording is at stake. What differs, and what
/// the user is actually choosing between, is ClipStudio's own metadata.
/// <para>
/// Tags and players use the bulk-edit chip model rather than a second way of reconciling them:
/// <see cref="BulkTagStatus.Shared"/> is on every copy, <see cref="BulkTagStatus.Partial"/> is on
/// some, and only <see cref="BulkTagStatus.New"/> is written. A partial chip therefore has to be
/// promoted to survive - which is explicit rather than guessed, but easy to get wrong in bulk, so
/// the screen says how many are at stake and offers to keep them all at once.
/// </para>
/// <para>
/// Highlights are a selection, not a merge: they are ranges over identical content, so every one
/// is valid against the survivor. They start selected, because losing one would lose work that the
/// file itself does not hold.
/// </para>
/// </remarks>
public sealed partial class DuplicateMergeViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDuplicateClipFinder _finder;
    private readonly DuplicateClipGroup _group;

    /// <summary>Gets the copies in the group, in the order they were found.</summary>
    public ObservableCollection<DuplicateMergeCandidateViewModel> Candidates { get; } = new();

    /// <summary>Gets the tag chips describing the group, in the bulk-edit model.</summary>
    public ObservableCollection<BulkTagChipViewModel> TagChips { get; } = new();

    /// <summary>Gets the player chips describing the group, in the bulk-edit model.</summary>
    public ObservableCollection<BulkTagChipViewModel> PlayerChips { get; } = new();

    /// <summary>Gets every highlight from every copy, deduplicated by range and label.</summary>
    public ObservableCollection<MergeHighlightViewModel> Highlights { get; } = new();

    /// <summary>Gets or sets the copy that survives the merge.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(RemovalSummary))]
    private DuplicateMergeCandidateViewModel? _survivor;

    /// <summary>Gets or sets whether the merge is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private bool _isApplying;

    /// <summary>Gets or sets whether the group could not be loaded.</summary>
    [ObservableProperty]
    private string? _loadError;

    /// <summary>Gets or sets whether the destructive step has been confirmed.</summary>
    /// <remarks>
    /// Removing the copies is the one irreversible-feeling part, so it is a deliberate second
    /// action rather than something Apply does because the dialog was open.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private bool _removalConfirmed;

    /// <summary>Gets whether the merge can be applied.</summary>
    public bool CanApply => Survivor is not null && RemovalConfirmed && !IsApplying;

    /// <summary>Gets how many tags and players exist only on a copy and would be lost.</summary>
    public int AtRiskCount => TagChips.Count(c => c.IsPartial) + PlayerChips.Count(c => c.IsPartial);

    /// <summary>Gets whether anything would be lost as things stand.</summary>
    public bool HasAtRisk => AtRiskCount > 0;

    /// <summary>Gets the warning naming what would be lost.</summary>
    public string AtRiskWarning => AtRiskCount == 1
        ? "1 tag or player is on only some of these copies. It will be lost unless you keep it."
        : $"{AtRiskCount} tags or players are on only some of these copies. They will be lost unless you keep them.";

    /// <summary>Gets a description of what will happen to the copies that do not survive.</summary>
    public string RemovalSummary
    {
        get
        {
            var others = Candidates.Count - 1;
            return others == 1
                ? "The other copy will be moved to the Trash, where it can be restored for 30 days."
                : $"The other {others} copies will be moved to the Trash, where they can be restored for 30 days.";
        }
    }

    /// <summary>Gets the command that promotes every partial chip, keeping all the metadata.</summary>
    public IRelayCommand KeepEverythingCommand { get; }

    /// <summary>Gets the command that applies the merge.</summary>
    public IAsyncRelayCommand ApplyCommand { get; }

    /// <summary>Gets the command that closes the dialog without changing anything.</summary>
    public IRelayCommand CancelCommand { get; }

    /// <summary>
    /// Closes the dialog, reporting whether the merge was applied. Set by the dialog view.
    /// </summary>
    public Action<bool>? CloseRequested { get; set; }

    /// <summary>Initialises a new <see cref="DuplicateMergeViewModel"/>.</summary>
    /// <param name="group">The duplicate group to resolve.</param>
    /// <param name="scopeFactory">The scope factory used to read and write the library.</param>
    /// <param name="finder">The finder, told to forget the group once it is resolved.</param>
    public DuplicateMergeViewModel(
        DuplicateClipGroup group,
        IServiceScopeFactory scopeFactory,
        IDuplicateClipFinder finder)
    {
        _group        = group;
        _scopeFactory = scopeFactory;
        _finder       = finder;

        KeepEverythingCommand = new RelayCommand(KeepEverything);
        ApplyCommand          = new AsyncRelayCommand(ApplyAsync, () => CanApply);
        CancelCommand         = new RelayCommand(() => CloseRequested?.Invoke(false));

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanApply))
                ApplyCommand.NotifyCanExecuteChanged();
        };
    }

    // ---- Load ----

    /// <summary>
    /// Loads the group's clips with the metadata the user is choosing between.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var clipService   = scope.ServiceProvider.GetRequiredService<IClipService>();
            var playerService = scope.ServiceProvider.GetRequiredService<IPlayerService>();

            Candidates.Clear();

            foreach (var clipId in _group.ClipIds)
            {
                var clip = await clipService.GetByIdAsync(clipId);
                if (clip is null)
                    continue;

                var players = await playerService.GetByClipAsync(clipId);
                Candidates.Add(new DuplicateMergeCandidateViewModel(clip, players));
            }

            if (Candidates.Count < 2)
            {
                // Something changed under us - a copy was trashed or relocated - so there is no
                // longer a duplicate to resolve.
                LoadError = "These clips are no longer duplicates. Run Repair Library again.";
                return;
            }

            // The first copy is only a starting point, not a recommendation; the user picks.
            Survivor = Candidates[0];
            Survivor.IsSurvivor = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load the duplicate group for merging.");
            LoadError = "The duplicate group could not be loaded.";
        }
    }

    partial void OnSurvivorChanged(DuplicateMergeCandidateViewModel? value)
    {
        foreach (var candidate in Candidates)
            candidate.IsSurvivor = ReferenceEquals(candidate, value);

        RebuildChips();
        RebuildHighlights();
        OnPropertyChanged(nameof(RemovalSummary));
    }

    /// <summary>
    /// Rebuilds the tag and player chips for the group, using the bulk-edit statuses.
    /// </summary>
    private void RebuildChips()
    {
        TagChips.Clear();
        PlayerChips.Clear();

        var total = Candidates.Count;

        foreach (var (id, name, count) in Collect(c => c.Tags))
            TagChips.Add(BuildChip(id, name, count, total, TagChips));

        foreach (var (id, name, count) in Collect(c => c.Players))
            PlayerChips.Add(BuildChip(id, name, count, total, PlayerChips));

        OnPropertyChanged(nameof(AtRiskCount));
        OnPropertyChanged(nameof(HasAtRisk));
        OnPropertyChanged(nameof(AtRiskWarning));
    }

    private BulkTagChipViewModel BuildChip(
        int id, string name, int presentOn, int total, ObservableCollection<BulkTagChipViewModel> owner)
    {
        var status = presentOn == total ? BulkTagStatus.Shared : BulkTagStatus.Partial;

        return new BulkTagChipViewModel(
            id, name, status,
            promote: status == BulkTagStatus.Partial
                ? chip =>
                {
                    chip.Status = BulkTagStatus.New;
                    OnPropertyChanged(nameof(AtRiskCount));
                    OnPropertyChanged(nameof(HasAtRisk));
                    OnPropertyChanged(nameof(AtRiskWarning));
                }
                : null,
            remove: chip =>
            {
                owner.Remove(chip);
                OnPropertyChanged(nameof(AtRiskCount));
                OnPropertyChanged(nameof(HasAtRisk));
                OnPropertyChanged(nameof(AtRiskWarning));
            });
    }

    /// <summary>
    /// Counts how many copies carry each tag or player, which is what decides a chip's status.
    /// </summary>
    /// <param name="select">Picks the tags or the players out of a candidate.</param>
    /// <returns>Each entity with its name and how many copies have it, ordered by name.</returns>
    private IEnumerable<(int Id, string Name, int Count)> Collect(
        Func<DuplicateMergeCandidateViewModel, IReadOnlyList<(int Id, string Name)>> select)
    {
        var names  = new Dictionary<int, string>();
        var counts = new Dictionary<int, int>();

        foreach (var candidate in Candidates)
        {
            foreach (var (id, name) in select(candidate).DistinctBy(e => e.Id))
            {
                counts[id] = counts.GetValueOrDefault(id) + 1;
                names.TryAdd(id, name);
            }
        }

        return counts
            .Select(kv => (kv.Key, names[kv.Key], kv.Value))
            .OrderBy(t => t.Item2, StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// Rebuilds the highlight list for the chosen survivor, collapsing copies of one range.
    /// </summary>
    /// <remarks>
    /// Two copies of the same recording usually carry the same highlights, so listing both would
    /// invite the user to create a duplicate range on the survivor. Identical range and label is
    /// treated as one entry, preferring the survivor's own so nothing is recreated needlessly.
    /// </remarks>
    private void RebuildHighlights()
    {
        Highlights.Clear();
        if (Survivor is null) return;

        var seen = new HashSet<(TimeSpan, TimeSpan, string)>();

        // The survivor first, so its own instance is the one that represents a shared range.
        var ordered = Candidates
            .OrderByDescending(c => ReferenceEquals(c, Survivor))
            .ToList();

        foreach (var candidate in ordered)
        {
            foreach (var highlight in candidate.Highlights.OrderBy(h => h.StartTime))
            {
                var key = (highlight.StartTime, highlight.EndTime, highlight.Label ?? string.Empty);
                if (!seen.Add(key))
                    continue;

                Highlights.Add(new MergeHighlightViewModel(
                    highlight,
                    isOnSurvivor: ReferenceEquals(candidate, Survivor),
                    originFileName: candidate.FileName));
            }
        }
    }

    /// <summary>Promotes every partial chip, so nothing on any copy is lost.</summary>
    private void KeepEverything()
    {
        foreach (var chip in TagChips.Where(c => c.IsPartial).ToList())
            chip.Status = BulkTagStatus.New;

        foreach (var chip in PlayerChips.Where(c => c.IsPartial).ToList())
            chip.Status = BulkTagStatus.New;

        OnPropertyChanged(nameof(AtRiskCount));
        OnPropertyChanged(nameof(HasAtRisk));
        OnPropertyChanged(nameof(AtRiskWarning));
    }

    // ---- Apply ----

    /// <summary>
    /// Writes the chosen metadata onto the survivor, then trashes the other copies.
    /// </summary>
    /// <remarks>
    /// Strictly in that order. If the removal failed halfway, the survivor would still be complete;
    /// doing it the other way round could lose metadata that no longer had anywhere to come from.
    /// </remarks>
    private async Task ApplyAsync()
    {
        if (Survivor is null) return;

        IsApplying = true;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var clipService      = scope.ServiceProvider.GetRequiredService<IClipService>();
            var playerService    = scope.ServiceProvider.GetRequiredService<IPlayerService>();
            var highlightService = scope.ServiceProvider.GetRequiredService<IHighlightService>();

            var survivorId = Survivor.ClipId;

            foreach (var chip in TagChips.Where(c => c.IsNew))
                await clipService.AddTagAsync(survivorId, chip.EntityId);

            foreach (var chip in PlayerChips.Where(c => c.IsNew))
                await playerService.TagClipAsync(survivorId, chip.EntityId);

            await ApplyHighlightsAsync(highlightService, survivorId);

            // Destructive, and last.
            foreach (var candidate in Candidates.Where(c => c.ClipId != survivorId))
                await clipService.TrashAsync(candidate.ClipId);

            _finder.Forget(_group.FullHash);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Merging duplicate clips failed.");
            LoadError = "The merge could not be completed. Nothing further was changed.";
        }
        finally
        {
            IsApplying = false;
        }
    }

    /// <summary>
    /// Gives the survivor the highlights the user selected, and takes away the ones they did not.
    /// </summary>
    /// <param name="highlights">The highlight service.</param>
    /// <param name="survivorId">The surviving clip.</param>
    private async Task ApplyHighlightsAsync(IHighlightService highlights, int survivorId)
    {
        foreach (var row in Highlights)
        {
            if (row.IsOnSurvivor)
            {
                // Already there: the only thing selection can mean is whether it stays.
                if (!row.IsSelected)
                    await highlights.DeleteAsync(row.HighlightId);

                continue;
            }

            if (!row.IsSelected)
                continue;

            var created = await highlights.CreateAsync(
                survivorId, row.Source.StartTime, row.Source.EndTime,
                row.Source.Label, row.Source.Notes);

            foreach (var tag in row.Source.HighlightTags.Where(ht => ht.Tag is not null))
                await highlights.AddTagAsync(created.Id, tag.TagId);

            if (row.Source.Rating > 0)
                await highlights.SetRatingAsync(created.Id, row.Source.Rating);

            if (row.Source.IsFavorite)
                await highlights.ToggleFavoriteAsync(created.Id);
        }
    }
}
