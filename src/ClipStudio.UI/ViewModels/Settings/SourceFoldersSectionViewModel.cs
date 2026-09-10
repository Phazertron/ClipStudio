using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using ClipStudio.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace ClipStudio.UI.ViewModels.Settings;

/// <summary>
/// The Source Folders section of the Settings page: the watched folder list, adding and removing
/// folders, and the manual scans that import from them.
/// </summary>
public sealed partial class SourceFoldersSectionViewModel : SettingsSectionViewModel
{
    private readonly ISourceFolderRepository _folders;
    private readonly ILibraryWatcherService _watcher;
    private readonly IImportService _importService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISoundService _soundService;

    /// <summary>Tracks the number of folder scans currently in progress.</summary>
    private int _activeScanCount;

    /// <inheritdoc/>
    public override string Title => "Source Folders";

    /// <inheritdoc/>
    public override MaterialIconKind Icon => MaterialIconKind.FolderMultipleOutline;

    /// <summary>Gets the collection of configured source folders.</summary>
    public ObservableCollection<SourceFolderRowViewModel> SourceFolders { get; } = new();

    /// <summary>Gets or sets the path entered in the add-folder text box.</summary>
    [ObservableProperty]
    private string _newFolderPath = string.Empty;

    /// <summary>Gets or sets the error message from the last add-folder attempt, if any.</summary>
    [ObservableProperty]
    private string? _folderError;

    /// <summary>Gets the command that adds the path in <see cref="NewFolderPath"/> as a new watched folder.</summary>
    public IAsyncRelayCommand AddFolderCommand { get; }

    /// <summary>Gets the command that scans all configured folders for new clips.</summary>
    public IAsyncRelayCommand ScanAllCommand { get; }

    /// <summary>
    /// Asks the user what to do about one duplicate file. Set by the section view's code-behind,
    /// which owns the window a dialog needs; left null in tests and headless contexts, where every
    /// duplicate is skipped rather than silently imported.
    /// </summary>
    public Func<DuplicateClipPrompt, Task<DuplicateResolution>>? DuplicateResolutionRequested { get; set; }

    /// <summary>Initialises a new <see cref="SourceFoldersSectionViewModel"/>.</summary>
    /// <param name="host">The settings page hosting this section.</param>
    /// <param name="folders">The source folder repository.</param>
    /// <param name="watcher">The library watcher service.</param>
    /// <param name="importService">The import service used to scan folders for existing clips.</param>
    /// <param name="scopeFactory">The scope factory used to resolve scoped services for a write.</param>
    /// <param name="soundService">The service that plays the import-complete cue.</param>
    public SourceFoldersSectionViewModel(
        ISettingsSectionHost host,
        ISourceFolderRepository folders,
        ILibraryWatcherService watcher,
        IImportService importService,
        IServiceScopeFactory scopeFactory,
        ISoundService soundService)
        : base(host)
    {
        _folders       = folders;
        _watcher       = watcher;
        _importService = importService;
        _scopeFactory  = scopeFactory;
        _soundService  = soundService;

        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        ScanAllCommand   = new AsyncRelayCommand(ScanAllAsync);
    }

    /// <summary>
    /// Reloads the folder rows from the database.
    /// </summary>
    /// <remarks>
    /// The list is left alone while a scan is running. The user may have navigated away and come
    /// back, and discarding the rows would take their in-progress scan state with it.
    /// </remarks>
    public override async Task RefreshAsync()
    {
        if (_activeScanCount != 0)
            return;

        var all = await _folders.GetAllAsync();
        SourceFolders.Clear();

        foreach (var folder in all)
            SourceFolders.Add(BuildRow(folder));
    }

    // ---- Source folder operations ----

    private async Task AddFolderAsync()
    {
        FolderError = null;

        var path = NewFolderPath.Trim();
        if (string.IsNullOrEmpty(path))
        {
            FolderError = "Please enter a folder path.";
            return;
        }

        if (!Directory.Exists(path))
        {
            FolderError = "Directory does not exist.";
            return;
        }

        // Strip any trailing directory separators before normalising so that a path with and
        // without a trailing slash is treated as the same folder.
        path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Prevent duplicate source folders (case-insensitive, normalised path comparison).
        var normalised = Path.GetFullPath(path);
        var existing   = await _folders.GetAllAsync();
        if (existing.Any(f => string.Equals(Path.GetFullPath(f.Path), normalised, StringComparison.OrdinalIgnoreCase)))
        {
            FolderError = "This folder is already in your library.";
            return;
        }

        var folder = new SourceFolder { Path = path, IsActive = true };
        await _folders.AddAsync(folder);
        _watcher.StartWatching(folder.Path, folder.Id);

        NewFolderPath = string.Empty;
        await Host.ReloadAsync();
    }

    /// <summary>
    /// Archives all clips belonging to the given source folder (status to Archived),
    /// stops watching the folder, and marks it inactive in the database.
    /// The folder record is intentionally kept so that the FK from Clip.SourceFolderId is not
    /// violated, and so that the user can re-enable the folder from Settings.
    /// Called by the section view after the user confirms Archive in the removal dialog.
    /// </summary>
    /// <param name="row">The folder row to archive.</param>
    public async Task ArchiveFolderAsync(SourceFolderRowViewModel row)
    {
        // All writes (clip archive + folder deactivation) share the same scoped DbContext so
        // there is no cross-context FK visibility window that could cause a crash.
        using var scope = _scopeFactory.CreateScope();
        var clipService = scope.ServiceProvider.GetRequiredService<IClipService>();
        var folderRepo  = scope.ServiceProvider.GetRequiredService<ISourceFolderRepository>();
        await clipService.ArchiveBySourceFolderAsync(row.FolderId);
        _watcher.StopWatching(row.Path);

        var folder = await folderRepo.GetByIdAsync(row.FolderId);
        if (folder is not null)
        {
            folder.IsActive = false;
            await folderRepo.UpdateAsync(folder);
        }

        Host.RequestUnreviewedCountRefresh();
        await Host.ReloadAsync();
    }

    /// <summary>
    /// Permanently deletes all clip records and media-cache files belonging to the given
    /// source folder, then stops watching the folder and removes it from the database.
    /// Source video files on disk are not touched.
    /// Called by the section view after the user confirms Wipe in the removal dialog.
    /// </summary>
    /// <param name="row">The folder row to wipe.</param>
    public async Task WipeFolderAsync(SourceFolderRowViewModel row)
    {
        // Clip wipe and folder deletion must share the same scoped DbContext so EF Core sees all
        // deletes in a single unit of work and the RESTRICT FK constraint is satisfied without
        // relying on cross-context transaction visibility.
        using var scope = _scopeFactory.CreateScope();
        var clipService = scope.ServiceProvider.GetRequiredService<IClipService>();
        var folderRepo  = scope.ServiceProvider.GetRequiredService<ISourceFolderRepository>();
        await clipService.WipeBySourceFolderAsync(row.FolderId);
        _watcher.StopWatching(row.Path);
        await folderRepo.DeleteAsync(row.FolderId);

        // Also delete the .clipstudio_trash subdirectory that lives inside the source folder,
        // since the clip DB records have been wiped and the physical trash files are now orphaned.
        try
        {
            var trashDir = Path.Combine(row.Path, ".clipstudio_trash");
            if (Directory.Exists(trashDir))
                Directory.Delete(trashDir, recursive: true);
        }
        catch (Exception ex)
        {
            // Non-fatal: log and continue so the folder record is still removed.
            Log.Warning(ex, "Could not delete the ClipStudio trash folder under {Path}.", row.Path);
        }

        Host.RequestUnreviewedCountRefresh();
        await Host.ReloadAsync();
    }

    private async Task RemoveFolderAsync(SourceFolderRowViewModel row)
    {
        _watcher.StopWatching(row.Path);
        await _folders.DeleteAsync(row.FolderId);
        await Host.ReloadAsync();
    }

    private async Task ToggleFolderActiveAsync(SourceFolderRowViewModel row)
    {
        using var scope = _scopeFactory.CreateScope();
        var folderRepo  = scope.ServiceProvider.GetRequiredService<ISourceFolderRepository>();

        var folder = await folderRepo.GetByIdAsync(row.FolderId);
        if (folder is null) return;

        folder.IsActive = !folder.IsActive;
        await folderRepo.UpdateAsync(folder);

        if (folder.IsActive)
        {
            // Restore: un-archive all clips that were archived when this folder was disabled so
            // they become visible in the library again.
            var clipService = scope.ServiceProvider.GetRequiredService<IClipService>();
            await clipService.UnarchiveBySourceFolderAsync(row.FolderId);
            _watcher.StartWatching(folder.Path, folder.Id);
        }
        else
        {
            _watcher.StopWatching(folder.Path);
        }

        row.IsActive = folder.IsActive;
    }

    // ---- Scanning ----

    /// <summary>
    /// Puts each duplicate a scan found to the user and imports the ones they want kept.
    /// </summary>
    /// <param name="folderId">The source folder the scan ran over.</param>
    /// <param name="results">The scan's results.</param>
    /// <returns>How many duplicates were imported and how many were skipped.</returns>
    private async Task<DuplicateResolutionSummary> ResolveDuplicatesAsync(
        int folderId, IReadOnlyList<ImportResult> results)
    {
        // With nowhere to ask, skipping is the safe answer: it leaves the file on disk and the
        // library unchanged, and the next scan will offer it again.
        var ask = DuplicateResolutionRequested
                  ?? (_ => Task.FromResult(DuplicateResolution.Skip));

        return await DuplicateImportResolver.ResolveAsync(
            results,
            ask,
            async path =>
            {
                var result = await _importService.ImportFileAsync(path, folderId, allowDuplicate: true);
                if (result.Clip is null)
                    Log.Warning("Importing duplicate {Path} failed: {Message}", path, result.Message);
                return result.Clip is not null;
            });
    }

    private async Task ScanFolderAsync(SourceFolderRowViewModel row)
    {
        _activeScanCount++;
        row.IsScanning          = true;
        row.ScanProgressPercent = 0;
        row.ScanStatusText      = "Starting scan...";
        row.LastScanSummary     = null;
        row.ScanErrorMessages.Clear();

        try
        {
            // Build a progress handler that marshals updates to the UI thread.
            var progress = new Progress<ImportProgressReport>(report =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!row.IsScanning)
                        return;

                    row.ScanProgressPercent = report.TotalFiles > 0
                        ? (double)report.CurrentFileIndex / report.TotalFiles * 100.0
                        : 0;

                    row.ScanStatusText =
                        $"File {report.CurrentFileIndex} / {report.TotalFiles} — {report.CurrentFileName}" +
                        $"  |  {report.Imported} imported" +
                        (report.Failed > 0 ? $", {report.Failed} failed" : string.Empty);
                });
            });

            var results = await _importService.ScanFolderAsync(row.FolderId, progress);

            // Import refuses duplicates rather than deciding for the user, so they are resolved
            // here - after the scan, so a long scan is never blocked waiting on a dialog.
            var duplicates = await ResolveDuplicatesAsync(row.FolderId, results);

            var imported = results.Count(r => r.Success && r.Clip != null) + duplicates.Imported;
            var failed   = results.Count(r => !r.Success);
            var skipped  = results.Count(r => r.Success && r.Clip == null && !r.IsDuplicate);

            row.LastScanSummary = $"{imported} imported, {skipped} already in library"
                + (duplicates.Skipped > 0 ? $", {duplicates.Skipped} duplicate(s) skipped" : string.Empty)
                + (failed > 0 ? $", {failed} failed" : string.Empty);

            if (imported > 0)
                _soundService.Play(SoundEffect.ImportComplete);

            foreach (var result in results.Where(r => !r.Success))
                row.ScanErrorMessages.Add(result.Message);

            // Refresh LastScannedDisplay from the updated entity.
            var updated = await _folders.GetByIdAsync(row.FolderId);
            if (updated?.LastScannedAt.HasValue == true)
                row.LastScannedDisplay = updated.LastScannedAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
        finally
        {
            row.IsScanning          = false;
            row.ScanStatusText      = null;
            row.ScanProgressPercent = 0;
            _activeScanCount--;
        }
    }

    /// <summary>
    /// Scans one source folder by identifier.
    /// </summary>
    /// <param name="folderId">The folder to scan.</param>
    /// <remarks>
    /// Offered so the attention list can act on "these files have not been imported" without
    /// duplicating any of the scan, its progress reporting or its duplicate resolution. A folder
    /// that is no longer in the list is silently ignored - the list may have moved on.
    /// </remarks>
    public async Task ScanFolderAsync(int folderId)
    {
        var row = SourceFolders.FirstOrDefault(r => r.FolderId == folderId);
        if (row is null) return;

        await ScanFolderAsync(row);
    }

    private async Task ScanAllAsync()
    {
        foreach (var row in SourceFolders.ToList())
            await ScanFolderAsync(row);

        Host.StatusMessage = "All folders scanned.";
        _soundService.Play(SoundEffect.ImportComplete);
    }

    // ---- Helpers ----

    private SourceFolderRowViewModel BuildRow(SourceFolder folder)
        => new(folder, RemoveFolderAsync, ToggleFolderActiveAsync, ScanFolderAsync);
}
