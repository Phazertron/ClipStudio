using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The Attention Required section: one list of everything in the library that needs a person to
/// decide about it.
/// </summary>
/// <remarks>
/// These findings existed before this section did, but had nowhere to live - they were logged, or
/// counted in a summary line that scrolled away, or marked on one page only. The section gathers
/// them: unreachable source folders and un-imported files from the startup health check, broken
/// clips and out-of-range highlights read straight from the library, and duplicate clips once a
/// repair has hashed them.
/// <para>
/// The rule for the whole section is that nothing is guessed at. Every entry prompts, and the
/// action it offers belongs to somewhere that already exists.
/// </para>
/// </remarks>
public sealed partial class AttentionSectionViewModel : SettingsSectionViewModel
{
    /// <summary>How many entries of one kind are listed individually before they are grouped.</summary>
    /// <remarks>
    /// A library that lost a whole folder can produce hundreds of missing-file entries. Listing
    /// them all buries everything else, so past this many they collapse into one row that says how
    /// many there are.
    /// </remarks>
    private const int MaxIndividualEntries = 8;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILibraryHealthCheckService _health;
    private readonly IAttentionActionHost _actions;

    /// <inheritdoc/>
    public override string Title => "Attention required";

    /// <inheritdoc/>
    public override MaterialIconKind Icon => MaterialIconKind.AlertCircleOutline;

    /// <summary>Gets the entries currently needing attention, most serious first.</summary>
    public ObservableCollection<AttentionEntryViewModel> Entries { get; } = new();

    /// <summary>Gets or sets how many entries the list holds.</summary>
    /// <remarks>Mirrored as a property so the navigation badge can bind to it.</remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEverythingFine))]
    private int _entryCount;

    /// <summary>Gets whether the library currently needs nothing from the user.</summary>
    public bool IsEverythingFine => EntryCount == 0;

    /// <summary>Gets or sets whether a re-check is running.</summary>
    [ObservableProperty]
    private bool _isChecking;

    /// <summary>Gets or sets when the list was last rebuilt, for display.</summary>
    [ObservableProperty]
    private string? _lastCheckedDisplay;

    /// <summary>Gets the command that re-runs the health check and rebuilds the list.</summary>
    public IAsyncRelayCommand RecheckCommand { get; }

    /// <summary>Raised whenever <see cref="EntryCount"/> changes, so a badge can follow it.</summary>
    public Action<int>? EntryCountChanged { get; set; }

    /// <summary>Initialises a new <see cref="AttentionSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    /// <param name="scopeFactory">The scope factory used to read the library.</param>
    /// <param name="health">The health check whose last report supplies the folder findings.</param>
    /// <param name="actions">The seam through which an entry's action reaches the rest of the app.</param>
    public AttentionSectionViewModel(
        ISettingsSectionHost host,
        IServiceScopeFactory scopeFactory,
        ILibraryHealthCheckService health,
        IAttentionActionHost actions)
        : base(host)
    {
        _scopeFactory = scopeFactory;
        _health       = health;
        _actions      = actions;

        RecheckCommand = new AsyncRelayCommand(RecheckAsync);
    }

    /// <inheritdoc/>
    public override Task RefreshAsync() => RebuildAsync();

    /// <summary>
    /// Runs a fresh health check and rebuilds the list from it.
    /// </summary>
    /// <remarks>
    /// The check is the cheap pass, not a repair: it re-reads the folders and the broken flags but
    /// does not hash, so it cannot find duplicates. Those arrive with Repair Library.
    /// </remarks>
    private async Task RecheckAsync()
    {
        IsChecking = true;
        try
        {
            await _health.CheckAsync();
            await RebuildAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The attention re-check failed.");
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>
    /// Rebuilds the list from the last health report plus what the library says right now.
    /// </summary>
    public async Task RebuildAsync()
    {
        var entries = new List<AttentionEntryViewModel>();

        try
        {
            AddFolderEntries(entries, _health.LastReport);
            await AddLibraryEntriesAsync(entries);
        }
        catch (Exception ex)
        {
            // The list is advisory. A failure to build it must not take the settings page with it.
            Log.Warning(ex, "Could not build the attention list.");
        }

        Entries.Clear();
        foreach (var entry in entries)
            Entries.Add(entry);

        EntryCount         = Entries.Count;
        LastCheckedDisplay = $"Last checked {DateTime.Now:HH:mm}";
        EntryCountChanged?.Invoke(EntryCount);
    }

    // ---- Sources ----

    /// <summary>
    /// Adds the entries that come from the startup health check: unreachable folders, and folders
    /// holding files that have never been imported.
    /// </summary>
    /// <param name="entries">The list being built.</param>
    /// <param name="report">The last health report, or null when no check has run.</param>
    /// <remarks>
    /// The report's missing-file findings are deliberately not used here. The broken flag on the
    /// clip row is the same information and is always current, so taking it from the library
    /// instead avoids listing a clip that has since been relocated.
    /// </remarks>
    private void AddFolderEntries(List<AttentionEntryViewModel> entries, LibraryHealthReport? report)
    {
        if (report is null) return;

        foreach (var finding in report.Findings.Where(f => f.Kind == LibraryHealthFindingKind.SourceFolderUnreachable))
        {
            entries.Add(new AttentionEntryViewModel(
                AttentionEntryKind.SourceFolderUnreachable,
                "A source folder cannot be reached",
                finding.Summary,
                MaterialIconKind.FolderRemoveOutline,
                "Open Source Folders",
                () =>
                {
                    _actions.ShowSourceFolders();
                    return Task.CompletedTask;
                }));
        }

        foreach (var finding in report.Findings.Where(f => f.Kind == LibraryHealthFindingKind.UnimportedFilesFound))
        {
            var folderId = finding.SourceFolderId;
            entries.Add(new AttentionEntryViewModel(
                AttentionEntryKind.UnimportedFiles,
                finding.Count == 1 ? "1 file has not been imported" : $"{finding.Count} files have not been imported",
                finding.Summary,
                MaterialIconKind.FileClockOutline,
                "Scan folder",
                folderId is null
                    ? null
                    : () => ScanAndRebuildAsync(folderId.Value)));
        }
    }

    /// <summary>
    /// Adds the entries read straight from the library: clips whose file is gone, and highlights
    /// whose range no longer fits their clip.
    /// </summary>
    /// <param name="entries">The list being built.</param>
    private async Task AddLibraryEntriesAsync(List<AttentionEntryViewModel> entries)
    {
        using var scope = _scopeFactory.CreateScope();
        var clips      = scope.ServiceProvider.GetRequiredService<IClipRepository>();
        var highlights = scope.ServiceProvider.GetRequiredService<IHighlightRepository>();

        var broken = (await clips.GetFileSnapshotsAsync()).Where(c => c.IsBroken).ToList();
        AddBrokenClipEntries(entries, broken);

        // The definition of "outside its clip" is the one watch mode already uses. Restating it
        // here would let the two drift, and it is the drift that locks the player up.
        var outOfRange = (await highlights.GetRangeSnapshotsAsync())
            .Where(h => !WatchWindow.Clamp(h.StartTime, h.EndTime, h.ClipDuration).IsUsable)
            .ToList();
        AddOutOfRangeHighlightEntries(entries, outOfRange);
    }

    private void AddBrokenClipEntries(
        List<AttentionEntryViewModel> entries, IReadOnlyList<ClipFileSnapshot> broken)
    {
        if (broken.Count == 0) return;

        if (broken.Count > MaxIndividualEntries)
        {
            entries.Add(new AttentionEntryViewModel(
                AttentionEntryKind.ClipFileMissing,
                $"{broken.Count} clips are missing their file",
                "Their source files are no longer where the library expects them. Open one from "
                + "the Library to relocate it, or remove them there.",
                MaterialIconKind.FileHidden));
            return;
        }

        foreach (var clip in broken)
        {
            entries.Add(new AttentionEntryViewModel(
                AttentionEntryKind.ClipFileMissing,
                $"'{clip.FileName}' is missing its file",
                $"The library expects it at {clip.FilePath}.",
                MaterialIconKind.FileHidden,
                "Open clip",
                () =>
                {
                    _actions.OpenClip(clip.Id);
                    return Task.CompletedTask;
                }));
        }
    }

    private void AddOutOfRangeHighlightEntries(
        List<AttentionEntryViewModel> entries, IReadOnlyList<HighlightRangeSnapshot> outOfRange)
    {
        if (outOfRange.Count == 0) return;

        if (outOfRange.Count > MaxIndividualEntries)
        {
            entries.Add(new AttentionEntryViewModel(
                AttentionEntryKind.HighlightOutOfRange,
                $"{outOfRange.Count} highlights fall outside their clip",
                "Each one needs a new range chosen by hand - there is no correct position to move "
                + "them to. Open the clips from the Library to re-pick them.",
                MaterialIconKind.BookmarkOffOutline));
            return;
        }

        foreach (var highlight in outOfRange)
        {
            var label = string.IsNullOrWhiteSpace(highlight.Label) ? "(unlabelled)" : highlight.Label;

            entries.Add(new AttentionEntryViewModel(
                AttentionEntryKind.HighlightOutOfRange,
                $"Highlight '{label}' falls outside its clip",
                $"It runs {Format(highlight.StartTime)}-{Format(highlight.EndTime)} but "
                + $"'{highlight.ClipFileName}' ends at {Format(highlight.ClipDuration)}. "
                + "It needs a new range chosen by hand.",
                MaterialIconKind.BookmarkOffOutline,
                "Open clip",
                () =>
                {
                    _actions.OpenClip(highlight.ClipId);
                    return Task.CompletedTask;
                }));
        }
    }

    // ---- Actions ----

    private async Task ScanAndRebuildAsync(int sourceFolderId)
    {
        await _actions.ScanSourceFolderAsync(sourceFolderId);
        await RecheckAsync();
    }

    /// <summary>Formats a time span the way the highlight rows do.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>A <c>h:mm:ss</c> or <c>m:ss</c> string.</returns>
    private static string Format(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");
}
