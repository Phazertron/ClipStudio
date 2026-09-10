using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using ClipStudio.UI.Services;
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
    private readonly IDuplicateClipFinder _duplicates;
    private readonly IAttentionActionHost _actions;
    private readonly IBackgroundTaskService _tasks;
    private readonly IApplicationUpdateService _updates;

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

    /// <summary>Gets the command that searches the library for duplicate clips.</summary>
    /// <remarks>
    /// Separate from <see cref="RecheckCommand"/> because it is a different kind of work: the
    /// re-check is directory listings, while this reads every candidate file end to end to confirm
    /// a match. It is also offered here rather than only inside Repair Library because the result
    /// lives in memory - after a restart the groups are gone until something looks again, and
    /// making the user run a full repair for that would be a poor trade.
    /// </remarks>
    public IAsyncRelayCommand ScanForDuplicatesCommand { get; }

    /// <summary>Gets or sets whether a duplicate scan is running.</summary>
    [ObservableProperty]
    private bool _isScanningForDuplicates;

    /// <summary>Gets or sets the outcome of the last duplicate scan, for display.</summary>
    [ObservableProperty]
    private string? _duplicateScanResult;

    /// <summary>Raised whenever <see cref="EntryCount"/> changes, so a badge can follow it.</summary>
    public Action<int>? EntryCountChanged { get; set; }

    /// <summary>
    /// Opens the merge screen for one duplicate group and reports whether it was applied. Set by
    /// the section view's code-behind, which owns the window a dialog needs; left null in tests,
    /// where a merge simply cannot be started.
    /// </summary>
    public Func<DuplicateClipGroup, Task<bool>>? MergeRequested { get; set; }

    /// <summary>Initialises a new <see cref="AttentionSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    /// <param name="scopeFactory">The scope factory used to read the library.</param>
    /// <param name="health">The health check whose last report supplies the folder findings.</param>
    /// <param name="duplicates">The finder whose last run supplies the duplicate groups.</param>
    /// <param name="actions">The seam through which an entry's action reaches the rest of the app.</param>
    /// <param name="tasks">The registry the duplicate scan reports itself into.</param>
    /// <param name="updates">The updater whose finding supplies the update entry.</param>
    public AttentionSectionViewModel(
        ISettingsSectionHost host,
        IServiceScopeFactory scopeFactory,
        ILibraryHealthCheckService health,
        IDuplicateClipFinder duplicates,
        IAttentionActionHost actions,
        IBackgroundTaskService tasks,
        IApplicationUpdateService updates)
        : base(host)
    {
        _scopeFactory = scopeFactory;
        _health       = health;
        _duplicates   = duplicates;
        _actions      = actions;
        _tasks        = tasks;
        _updates      = updates;

        // The check runs at startup and can finish long after this section was first built, so
        // the list follows it rather than only reflecting what was known when Settings opened.
        _updates.StatusChanged += OnUpdateStatusChanged;

        RecheckCommand           = new AsyncRelayCommand(RecheckAsync);
        ScanForDuplicatesCommand = new AsyncRelayCommand(ScanForDuplicatesAsync);
    }

    /// <summary>
    /// Rebuilds the list on the UI thread when the updater reports a new status.
    /// </summary>
    /// <param name="sender">The updater.</param>
    /// <param name="status">The status just published.</param>
    /// <remarks>
    /// The updater publishes from a background task, so the hop to the UI thread is required
    /// before touching the observable collection.
    /// </remarks>
    private void OnUpdateStatusChanged(object? sender, UpdateStatus status)
        => Dispatcher.UIThread.Post(() => _ = RebuildAsync());

    /// <inheritdoc/>
    public override Task RefreshAsync() => RebuildAsync();

    /// <summary>
    /// Re-checks everything this list can show, then rebuilds it.
    /// </summary>
    /// <remarks>
    /// Covers both sources the list draws on: the library health check, which re-reads the source
    /// folders from disk and is the only way to notice a reconnected drive without restarting, and
    /// the update check. Leaving the update out made a button labelled "Re-check" quietly ignore
    /// rows it was displaying.
    /// <para>
    /// The health check is the cheap pass, not a repair: it re-reads the folders and the broken
    /// flags but does not hash, so it cannot find duplicates. Those arrive with Repair Library.
    /// The update check downloads nothing.
    /// </para>
    /// </remarks>
    private async Task RecheckAsync()
    {
        IsChecking = true;
        try
        {
            await _health.CheckAsync();

            // Never throws, and is deliberately not allowed to stop the folder findings being
            // rebuilt if the network is down.
            await _updates.CheckAsync();

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
    /// Searches the library for clips that are the same recording, then rebuilds the list.
    /// </summary>
    private async Task ScanForDuplicatesAsync()
    {
        IsScanningForDuplicates = true;
        DuplicateScanResult     = null;

        // This one reads whole files to confirm a match, so it is the scan most worth being able
        // to stop - and the one most worth seeing from another page while it runs.
        using var cancellation = new CancellationTokenSource();
        var task = _tasks.Start(
            "Looking for duplicate clips",
            MaterialIconKind.ContentDuplicate,
            ownerNavLabel: "Settings",
            cancellation: cancellation);

        var progress = new Progress<string>(msg =>
            Dispatcher.UIThread.Post(() => task.Report(msg)));

        try
        {
            var groups = await _duplicates.FindAsync(progress, cancellation.Token);
            DuplicateScanResult = groups.Count == 0
                ? "No duplicate clips found."
                : $"Found {groups.Sum(g => g.ClipIds.Count)} clips in {groups.Count} group(s).";

            task.Finish(BackgroundTaskState.Completed, DuplicateScanResult);
            await RebuildAsync();
        }
        catch (OperationCanceledException)
        {
            // A partial scan would list some groups and silently omit others, which reads as "these
            // are the duplicates" when it is not. Report nothing rather than something misleading.
            DuplicateScanResult = "Duplicate scan cancelled.";
            task.Finish(BackgroundTaskState.Cancelled, "Scan stopped before it could compare everything.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The duplicate scan failed.");
            DuplicateScanResult = "The duplicate scan could not be completed.";
            task.Finish(BackgroundTaskState.Failed, ex.Message);
        }
        finally
        {
            IsScanningForDuplicates = false;
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
            AddUpdateEntry(entries);
            AddFolderEntries(entries, _health.LastReport);
            AddDuplicateEntries(entries);
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
    /// Adds the entry offering a downloaded application update, when one is waiting.
    /// </summary>
    /// <param name="entries">The list being built.</param>
    /// <remarks>
    /// Added first so it sits at the top: it is the only entry the user can clear in one click,
    /// and an out-of-date build may be the reason some of the entries below it exist.
    /// <para>
    /// Only offered once the package has finished downloading. Announcing a version that is still
    /// transferring would put a "Restart and install" button in front of the user that cannot do
    /// anything yet.
    /// </para>
    /// </remarks>
    private void AddUpdateEntry(List<AttentionEntryViewModel> entries)
    {
        var status = _updates.Status;
        if (!status.IsSupported || !status.IsUpdateAvailable)
            return;

        var running = status.CurrentVersion ?? "an earlier version";

        if (status.IsDownloading)
        {
            // The download reports itself into the task registry, which is where its progress bar
            // and its cancel button live. Repeating either here would be a second control for one
            // operation, so this row only says what is happening.
            entries.Add(new AttentionEntryViewModel(
                AttentionEntryKind.UpdateAvailable,
                $"Downloading ClipStudio {status.AvailableVersion}",
                "Progress is shown in the task panel, where it can also be cancelled.",
                MaterialIconKind.Download));
            return;
        }

        if (status.IsDownloaded)
        {
            entries.Add(new AttentionEntryViewModel(
                AttentionEntryKind.UpdateAvailable,
                $"ClipStudio {status.AvailableVersion} is ready to install",
                $"You are running {running}. Installing it restarts the application.",
                MaterialIconKind.Download,
                "Restart and install",
                () =>
                {
                    _updates.ApplyAndRestart();
                    return Task.CompletedTask;
                }));
            return;
        }

        entries.Add(new AttentionEntryViewModel(
            AttentionEntryKind.UpdateAvailable,
            $"ClipStudio {status.AvailableVersion} is available",
            $"You are running {running}. Nothing has been downloaded yet - an update package can "
            + "run to several hundred megabytes, so it waits to be asked for.",
            MaterialIconKind.Download,
            "Download update",
            DownloadUpdateAsync));
    }

    /// <summary>
    /// Downloads the available update, reporting into the task registry so its progress is
    /// visible from anywhere and can be cancelled.
    /// </summary>
    private async Task DownloadUpdateAsync()
    {
        using var cancellation = new CancellationTokenSource();

        var task = _tasks.Start(
            $"Downloading ClipStudio {_updates.Status.AvailableVersion}",
            MaterialIconKind.Download,
            ownerNavLabel: "Settings",
            cancellation: cancellation);

        var progress = new Progress<int>(percent =>
            Dispatcher.UIThread.Post(() => task.Report(percent, $"{percent}%")));

        try
        {
            var downloaded = await _updates.DownloadAsync(progress, cancellation.Token);

            task.Finish(
                downloaded ? BackgroundTaskState.Completed : BackgroundTaskState.Failed,
                downloaded
                    ? "Ready to install. Restart ClipStudio to apply it."
                    : _updates.Status.FailureReason ?? "The update could not be downloaded.");
        }
        catch (OperationCanceledException)
        {
            task.Finish(BackgroundTaskState.Cancelled, "Download stopped. The update is still available.");
        }

        await RebuildAsync();
    }

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
    /// Adds one entry per group of clips that are the same recording.
    /// </summary>
    /// <param name="entries">The list being built.</param>
    /// <remarks>
    /// Read from the finder's last run rather than searched for here. Confirming a duplicate means
    /// reading both files end to end, so it belongs to Repair Library; until one has run, this
    /// contributes nothing and the section says so by simply not listing any.
    /// </remarks>
    private void AddDuplicateEntries(List<AttentionEntryViewModel> entries)
    {
        foreach (var group in _duplicates.LastGroups)
        {
            var count    = group.ClipIds.Count;
            var captured = group;

            entries.Add(new AttentionEntryViewModel(
                AttentionEntryKind.DuplicateClips,
                count == 2
                    ? "2 clips are the same recording"
                    : $"{count} clips are the same recording",
                "Their files hold identical contents. Only the ClipStudio metadata - tags, "
                + "players, highlights, rating and notes - differs between them.",
                MaterialIconKind.ContentDuplicate,
                "Merge...",
                MergeRequested is null ? null : () => MergeAndRebuildAsync(captured)));
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

    /// <summary>
    /// Opens the merge screen for one group and rebuilds the list if anything was applied.
    /// </summary>
    /// <param name="group">The group to resolve.</param>
    private async Task MergeAndRebuildAsync(DuplicateClipGroup group)
    {
        if (MergeRequested is null) return;

        if (await MergeRequested(group))
            await RebuildAsync();
    }

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
