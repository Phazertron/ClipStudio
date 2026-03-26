using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFMpegCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Settings page.
/// Registered as a singleton so that an in-progress import scan is not interrupted when
/// the user navigates away from the Settings tab and returns.
/// Manages source folder configuration (including manual scans) and application preferences.
/// Changes to preferences are persisted via <see cref="SavePreferencesCommand"/>.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ISourceFolderRepository _folders;
    private readonly ILibraryWatcherService _watcher;
    private readonly IImportService _importService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClipStudio.UI.Services.ISoundService _soundService;

    /// <summary>Tracks the number of folder scans currently in progress.</summary>
    /// <summary>Set to <see langword=true/> while a library repair is running.
    /// Prevents <see cref=LoadAsync/> from clearing <see cref=StatusMessage/> mid-repair.</summary>
    private bool _isRepairing;

    private int _activeScanCount;

    // ---- Source folders ----

    /// <summary>Gets the collection of configured source folders.</summary>
    public ObservableCollection<SourceFolderRowViewModel> SourceFolders { get; } = new();

    /// <summary>Gets or sets the path entered in the add-folder text box.</summary>
    [ObservableProperty]
    private string _newFolderPath = string.Empty;

    /// <summary>Gets or sets the error message from the last add-folder attempt, if any.</summary>
    [ObservableProperty]
    private string? _folderError;

    // ---- Preferences ----

    /// <summary>Gets or sets the offset in seconds from clip start used to capture the thumbnail frame.</summary>
    [ObservableProperty]
    private int _thumbnailOffsetSeconds;

    /// <summary>Gets or sets the absolute path where frame screenshots are saved.</summary>
    [ObservableProperty]
    private string _screenshotOutputFolder = string.Empty;

    /// <summary>
    /// Gets or sets the absolute path to the folder containing the FFmpeg binaries.
    /// Leave blank to use the system PATH or the auto-detected bundled <c>ffmpeg/</c> folder.
    /// </summary>
    [ObservableProperty]
    private string _ffmpegBinaryFolder = string.Empty;

    /// <summary>
    /// Gets or sets whether clips are automatically transitioned to Reviewed when the user
    /// adds their first tag to them.
    /// </summary>
    [ObservableProperty]
    private bool _autoMarkReviewedOnTagAdd = true;

    /// <summary>
    /// Gets or sets whether a clip starts playing automatically when it is opened in the detail view.
    /// </summary>
    [ObservableProperty]
    private bool _autoPlayOnOpen = true;

    /// <summary>
    /// Gets or sets whether all active source folders are scanned for new clips on each application start.
    /// </summary>
    [ObservableProperty]
    private bool _autoScanAtStartup;

    /// <summary>
    /// Gets or sets whether mixed audio preview files are cached on disk between sessions.
    /// </summary>
    [ObservableProperty]
    private bool _cacheAudioPreviews = true;

    /// <summary>
    /// Gets or sets whether clips removed from the Trash (by auto-expiry or Empty Trash) are
    /// sent to the OS Recycle Bin. When false, files are permanently deleted immediately.
    /// </summary>
    [ObservableProperty]
    private bool _trashExpiredSendToRecycleBin = true;

    /// <summary>
    /// Gets or sets whether players marked as "Me" are automatically tagged on every newly imported clip.
    /// </summary>
    [ObservableProperty]
    private bool _autoApplyMePlayerOnImport = true;

    /// <summary>
    /// Gets or sets whether game cover art and player icon images are shown in the library
    /// details view and highlights list. When disabled, plain text is shown instead.
    /// </summary>
    [ObservableProperty]
    private bool _showImagesInLists = true;

    /// <summary>
    /// Gets or sets the minimum log level for the rolling log file.
    /// Accepted values: "Verbose", "Debug", "Information", "Warning", "Error", "Fatal".
    /// </summary>
    [ObservableProperty]
    private string _minimumLogLevel = "Error";

    /// <summary>
    /// Gets or sets whether UI sound effects are played.
    /// </summary>
    [ObservableProperty]
    private bool _soundEffectsEnabled = true;

    // ---- Transcription ----

    /// <summary>Gets or sets whether local voice transcription is enabled.</summary>
    [ObservableProperty]
    private bool _transcriptionEnabled;

    /// <summary>Gets or sets the absolute path to the GGML Whisper model file.</summary>
    [ObservableProperty]
    private string _transcriptionModelPath = string.Empty;

    /// <summary>Gets or sets the transcription inference backend display string.</summary>
    [ObservableProperty]
    private string _transcriptionBackend = "Auto (recommended)";

    /// <summary>Gets or sets the language option selected for transcription.</summary>
    [ObservableProperty]
    private ClipStudio.UI.Views.LanguageOption _selectedTranscriptionLanguage = TranscriptionLanguageOptions[0];

    /// <summary>Gets or sets the folder where generated SRT files are saved (empty = next to clip).</summary>
    [ObservableProperty]
    private string _transcriptionSrtFolder = string.Empty;

    /// <summary>Gets or sets whether the experimental speaker diarization pass is enabled (no-op in v1).</summary>
    [ObservableProperty]
    private bool _transcriptionEnableDiarization;

    /// <summary>Gets the list of backend display strings for the ComboBox.</summary>
    public static IReadOnlyList<string> TranscriptionBackendOptions { get; } =
        new[] { "Auto (recommended)", "CPU only", "Vulkan (GPU)" };

    /// <summary>Gets the list of language options for the transcription language picker.</summary>
    public static IReadOnlyList<ClipStudio.UI.Views.LanguageOption> TranscriptionLanguageOptions { get; } =
        ClipStudio.UI.Views.TranscriptionSetupDialogViewModel.BuildLanguageOptionsList();

    // ---- State ----

    /// <summary>Gets or sets a value indicating whether a background operation is running.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Gets or sets a status message shown after saving preferences.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    // ---- Commands ----

    /// <summary>Gets the command that loads source folders and current settings from persistence.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that adds the path in <see cref="NewFolderPath"/> as a new watched folder.</summary>
    public IAsyncRelayCommand AddFolderCommand { get; }

    /// <summary>Gets the command that scans all configured folders for new clips.</summary>
    public IAsyncRelayCommand ScanAllCommand { get; }

    /// <summary>Gets the command that saves all preference fields to disk.</summary>
    public IAsyncRelayCommand SavePreferencesCommand { get; }

    /// <summary>Gets the command that opens the OBS scripts folder in Explorer.</summary>
    public IRelayCommand OpenOBSScriptsFolderCommand { get; }

    /// <summary>Gets the command that runs an on-demand library sanitize pass to regenerate missing
    /// thumbnails and preview strips and remove orphaned cache files.</summary>
    public IAsyncRelayCommand RepairLibraryCommand { get; }

    /// <summary>Gets the command that clears all cached audio mix preview files.</summary>
    public IRelayCommand ClearAudioCacheCommand { get; }

    /// <summary>Gets the command that opens the Ko-fi support page in the default browser.</summary>
    public IRelayCommand OpenKofiCommand { get; }

    /// <summary>Gets the command that opens the logs folder in the system file explorer.</summary>
    public IRelayCommand OpenLogsFolderCommand { get; }

    /// <summary>Gets the command that opens the GitHub Issues page to submit feedback.</summary>
    public IRelayCommand SendFeedbackCommand { get; }

    // ---- Callbacks ----

    /// <summary>
    /// Optional callback invoked after a source folder archive or wipe so that the main window
    /// can refresh the unreviewed clip count badge.
    /// </summary>
    public Action? UnreviewedCountRefreshRequested { get; set; }

    // ---- App info ----

    /// <summary>
    /// Gets the list of available log levels for the minimum log level ComboBox.
    /// Exposed as a static list so the AXAML ComboBox can bind to it as ItemsSource,
    /// allowing <see cref="MinimumLogLevel"/> (a plain string) to match correctly.
    /// </summary>
    public static IReadOnlyList<string> LogLevels { get; } =
        new[] { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" };

    /// <summary>
    /// Gets a human-readable application version string derived from the assembly version.
    /// </summary>
    public string AppVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "ClipStudio" : $"ClipStudio v{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    // ---- OBS Integration ----

    /// <summary>
    /// Gets the status of the bundled OBS Python script.
    /// Returns a message indicating whether the script file exists next to the application binary.
    /// </summary>
    public string OBSScriptStatus
    {
        get
        {
            var path = OBSScriptPath;
            return File.Exists(path)
                ? $"Script found: {path}"
                : $"Script not found at expected location: {path}";
        }
    }

    /// <summary>
    /// Gets the absolute path to the bundled OBS Python script,
    /// located in <c>obs-scripts/</c> next to the application executable.
    /// </summary>
    public string OBSScriptPath =>
        Path.Combine(AppContext.BaseDirectory, "obs-scripts", "clipstudio_replay_tagger.py");

    /// <summary>
    /// Exposes the underlying settings service so that code-behind can pass it to dialogs
    /// (e.g. <see cref="ClipStudio.UI.Views.TranscriptionSetupDialog"/>).
    /// </summary>
    public ISettingsService SettingsService => _settings;

    /// <summary>
    /// Initialises a new <see cref="SettingsViewModel"/>.
    /// </summary>
    /// <param name="settings">The application settings service.</param>
    /// <param name="folders">The source folder repository.</param>
    /// <param name="watcher">The library watcher service.</param>
    /// <param name="importService">The import service used to scan folders for existing clips.</param>
    /// <param name="scopeFactory">The service scope factory used to resolve scoped services such as <see cref="ILibrarySanitizerService"/>.</param>
    public SettingsViewModel(
        ISettingsService settings,
        ISourceFolderRepository folders,
        ILibraryWatcherService watcher,
        IImportService importService,
        IServiceScopeFactory scopeFactory,
        ClipStudio.UI.Services.ISoundService soundService)
    {
        _settings      = settings;
        _folders       = folders;
        _watcher       = watcher;
        _importService = importService;
        _scopeFactory  = scopeFactory;
        _soundService  = soundService;

        LoadCommand                  = new AsyncRelayCommand(LoadAsync);
        AddFolderCommand             = new AsyncRelayCommand(AddFolderAsync);
        ScanAllCommand               = new AsyncRelayCommand(ScanAllAsync);
        SavePreferencesCommand       = new AsyncRelayCommand(SavePreferencesAsync);
        OpenOBSScriptsFolderCommand  = new RelayCommand(OpenOBSScriptsFolder);
        RepairLibraryCommand         = new AsyncRelayCommand(RepairLibraryAsync);
        ClearAudioCacheCommand       = new RelayCommand(ClearAudioCache);
        OpenKofiCommand              = new RelayCommand(OpenKofi);
        OpenLogsFolderCommand        = new RelayCommand(OpenLogsFolder);
        SendFeedbackCommand          = new RelayCommand(SendFeedback);
    }

    // ---- Load ----

    /// <summary>
    /// Loads all source folders from the database and populates the preferences fields
    /// from the current settings snapshot.
    /// When one or more folder scans are active the folder list is not refreshed so that
    /// in-progress scan state is preserved — the user can safely switch tabs and return.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading     = true;
        if (!_isRepairing) StatusMessage = null;
        FolderError   = null;

        try
        {
            // Do not discard the folder list while a scan is running; the user may have
            // navigated away and come back.  Only refresh when the UI is idle.
            if (_activeScanCount == 0)
            {
                var all = await _folders.GetAllAsync();
                SourceFolders.Clear();

                foreach (var folder in all)
                    SourceFolders.Add(BuildRow(folder));
            }

            var s = _settings.Current;
            ThumbnailOffsetSeconds     = s.ThumbnailOffsetSeconds;
            ScreenshotOutputFolder     = s.ScreenshotOutputFolder ?? string.Empty;
            FfmpegBinaryFolder         = s.FfmpegBinaryFolder ?? string.Empty;
            AutoMarkReviewedOnTagAdd   = s.AutoMarkReviewedOnTagAdd;
            AutoPlayOnOpen             = s.AutoPlayOnOpen;
            AutoScanAtStartup              = s.AutoScanAtStartup;
            CacheAudioPreviews             = s.CacheAudioPreviews;
            TrashExpiredSendToRecycleBin   = s.TrashExpiredSendToRecycleBin;
            AutoApplyMePlayerOnImport      = s.AutoApplyMePlayerOnImport;
            ShowImagesInLists              = s.ShowImagesInLists;
            MinimumLogLevel                = s.MinimumLogLevel;
            SoundEffectsEnabled            = s.SoundEffectsEnabled;
            TranscriptionEnabled           = s.TranscriptionEnabled;
            TranscriptionModelPath         = s.TranscriptionModelPath;
            SelectedTranscriptionLanguage  = TranscriptionLanguageOptions
                                                .FirstOrDefault(o => o.Code == s.TranscriptionLanguage)
                                                ?? TranscriptionLanguageOptions[0];
            TranscriptionSrtFolder         = s.TranscriptionSrtFolder;
            TranscriptionEnableDiarization = s.TranscriptionEnableDiarization;
            TranscriptionBackend           = s.TranscriptionBackend switch
            {
                ClipStudio.Core.Enums.TranscriptionBackend.Cpu    => "CPU only",
                ClipStudio.Core.Enums.TranscriptionBackend.Vulkan => "Vulkan (GPU)",
                _                                                  => "Auto (recommended)"
            };
        }
        finally
        {
            IsLoading = false;
        }
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

        // Strip any trailing directory separators before normalising so that "E:\Foo\" and
        // "E:\Foo" are treated as the same folder.
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
        await LoadAsync();
    }

    /// <summary>
    /// Archives all clips belonging to the given source folder (status → Archived),
    /// stops watching the folder, and marks it inactive in the database.
    /// The folder record is intentionally kept so that the FK from Clip.SourceFolderId is not
    /// violated, and so that the user can re-enable the folder from Settings.
    /// Called by <see cref="SettingsView"/> after the user confirms Archive in the removal dialog.
    /// </summary>
    public async Task ArchiveFolderAsync(SourceFolderRowViewModel row)
    {
        // All writes (clip archive + folder deactivation) share the same scoped DbContext so
        // there is no cross-context FK visibility window that could cause a crash.
        using var scope      = _scopeFactory.CreateScope();
        var clipService      = scope.ServiceProvider.GetRequiredService<IClipService>();
        var folderRepo       = scope.ServiceProvider.GetRequiredService<ISourceFolderRepository>();
        await clipService.ArchiveBySourceFolderAsync(row.FolderId);
        _watcher.StopWatching(row.Path);

        var folder = await folderRepo.GetByIdAsync(row.FolderId);
        if (folder is not null)
        {
            folder.IsActive = false;
            await folderRepo.UpdateAsync(folder);
        }

        UnreviewedCountRefreshRequested?.Invoke();
        await LoadAsync();
    }

    /// <summary>
    /// Permanently deletes all clip records and media-cache files belonging to the given
    /// source folder, then stops watching the folder and removes it from the database.
    /// Source video files on disk are not touched.
    /// Called by <see cref="SettingsView"/> after the user confirms Wipe in the removal dialog.
    /// </summary>
    public async Task WipeFolderAsync(SourceFolderRowViewModel row)
    {
        // Clip wipe and folder deletion must share the same scoped DbContext so EF Core sees all
        // deletes in a single unit of work and the RESTRICT FK constraint is satisfied without
        // relying on cross-context transaction visibility.
        using var scope      = _scopeFactory.CreateScope();
        var clipService      = scope.ServiceProvider.GetRequiredService<IClipService>();
        var folderRepo       = scope.ServiceProvider.GetRequiredService<ISourceFolderRepository>();
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
            System.Diagnostics.Debug.WriteLine($"[ClipStudio] Could not delete .clipstudio_trash: {ex.Message}");
        }

        UnreviewedCountRefreshRequested?.Invoke();
        await LoadAsync();
    }

    private async Task RemoveFolderAsync(SourceFolderRowViewModel row)
    {
        _watcher.StopWatching(row.Path);
        await _folders.DeleteAsync(row.FolderId);
        await LoadAsync();
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

    private async Task ScanFolderAsync(SourceFolderRowViewModel row)
    {
        _activeScanCount++;
        row.IsScanning        = true;
        row.ScanProgressPercent = 0;
        row.ScanStatusText    = "Starting scan...";
        row.LastScanSummary   = null;
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
                        $"File {report.CurrentFileIndex} / {report.TotalFiles} \u2014 {report.CurrentFileName}" +
                        $"  |  {report.Imported} imported" +
                        (report.Failed > 0 ? $", {report.Failed} failed" : string.Empty);
                });
            });

            var results = await _importService.ScanFolderAsync(row.FolderId, progress);

            var imported = results.Count(r => r.Success && r.Clip != null);
            var skipped  = results.Count(r => r.Success && r.Clip == null);
            var failed   = results.Count(r => !r.Success);

            row.LastScanSummary = $"{imported} imported, {skipped} already in library"
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
            row.IsScanning        = false;
            row.ScanStatusText    = null;
            row.ScanProgressPercent = 0;
            _activeScanCount--;
        }
    }

    private async Task ScanAllAsync()
    {
        foreach (var row in SourceFolders.ToList())
            await ScanFolderAsync(row);

        StatusMessage = "All folders scanned.";
        _soundService.Play(SoundEffect.ImportComplete);
    }

    // ---- Save preferences ----

    private async Task SavePreferencesAsync()
    {
        StatusMessage = null;
        var s = _settings.Current;
        s.ThumbnailOffsetSeconds   = ThumbnailOffsetSeconds;
        s.ScreenshotOutputFolder   = ScreenshotOutputFolder.Trim();
        s.FfmpegBinaryFolder       = FfmpegBinaryFolder.Trim();
        s.AutoMarkReviewedOnTagAdd = AutoMarkReviewedOnTagAdd;
        s.AutoPlayOnOpen           = AutoPlayOnOpen;
        s.AutoScanAtStartup              = AutoScanAtStartup;
        s.CacheAudioPreviews             = CacheAudioPreviews;
        s.TrashExpiredSendToRecycleBin   = TrashExpiredSendToRecycleBin;
        s.AutoApplyMePlayerOnImport      = AutoApplyMePlayerOnImport;
        s.ShowImagesInLists              = ShowImagesInLists;
        s.MinimumLogLevel                = MinimumLogLevel;
        s.SoundEffectsEnabled            = SoundEffectsEnabled;
        s.TranscriptionEnabled           = TranscriptionEnabled;
        s.TranscriptionModelPath         = TranscriptionModelPath.Trim();
        s.TranscriptionLanguage          = SelectedTranscriptionLanguage.Code;
        s.TranscriptionSrtFolder         = TranscriptionSrtFolder.Trim();
        s.TranscriptionEnableDiarization = TranscriptionEnableDiarization;
        s.TranscriptionBackend           = TranscriptionBackend switch
        {
            "CPU only"     => ClipStudio.Core.Enums.TranscriptionBackend.Cpu,
            "Vulkan (GPU)" => ClipStudio.Core.Enums.TranscriptionBackend.Vulkan,
            _              => ClipStudio.Core.Enums.TranscriptionBackend.Auto
        };
        await _settings.SaveAsync();

        // Re-apply FFmpeg binary path immediately so scans after saving use the new value.
        ApplyFfmpegFolder(s.FfmpegBinaryFolder);

        StatusMessage = "Settings saved.";
    }

    /// <summary>
    /// Applies the FFmpeg binary folder to FFMpegCore's global options, auto-detecting the
    /// bundled <c>ffmpeg/</c> folder when no explicit path is configured.
    /// </summary>
    private static void ApplyFfmpegFolder(string configuredFolder)
    {
        var folder = configuredFolder;

        if (string.IsNullOrWhiteSpace(folder))
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            if (Directory.Exists(candidate))
                folder = candidate;
        }

        if (!string.IsNullOrWhiteSpace(folder))
            GlobalFFOptions.Configure(options => options.BinaryFolder = folder);
    }

    // ---- Library repair ----

    /// <summary>
    /// Runs an on-demand library sanitize pass that regenerates any missing thumbnail or preview-strip
    /// files and removes orphaned files from the media cache.
    /// Progress messages are surfaced via <see cref="StatusMessage"/>.
    /// </summary>
    private async Task RepairLibraryAsync()
    {
        _isRepairing  = true;
        IsLoading     = true;
        StatusMessage = "Repairing library...";

        var progress = new Progress<string>(msg =>
            Dispatcher.UIThread.Post(() => StatusMessage = msg));

        try
        {
            using var scope    = _scopeFactory.CreateScope();
            var sanitizer      = scope.ServiceProvider.GetRequiredService<ILibrarySanitizerService>();
            await sanitizer.SanitizeAsync(progress, CancellationToken.None);
        }
        finally
        {
            IsLoading = false;
            _isRepairing = false;
        }
    }

    // ---- OBS helpers ----

    /// <summary>
    /// Opens the OBS scripts folder (<c>%AppData%\obs-studio\scripts</c>) in the system file explorer.
    /// Creates the folder if it does not yet exist.
    /// </summary>
    private static void OpenOBSScriptsFolder()
    {
        var obsScripts = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obs-studio", "scripts");

        Directory.CreateDirectory(obsScripts);

        Process.Start(new ProcessStartInfo
        {
            FileName        = obsScripts,
            UseShellExecute = true,
        });
    }

    // ---- Audio cache ----

    /// <summary>
    /// Deletes all cached audio mix preview files from <c>%AppData%/ClipStudio/audio_cache/</c>.
    /// </summary>
    private void ClearAudioCache()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClipStudio", "audio_cache");

        if (!Directory.Exists(folder))
        {
            StatusMessage = "Audio cache folder not found — nothing to clear.";
            return;
        }

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*.mkv"))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch { /* non-fatal — file may be held by VLC */ }
        }

        StatusMessage = deleted == 0
            ? "Audio cache was already empty."
            : $"Cleared {deleted} audio cache file{(deleted == 1 ? string.Empty : "s")}.";
    }

    // ---- Support ----

    /// <summary>Opens the Ko-fi support page in the default browser.</summary>
    private static void OpenKofi()
    {
        Process.Start(new ProcessStartInfo("https://ko-fi.com/phazertron") { UseShellExecute = true });
    }

    // ---- Logs + Feedback ----

    /// <summary>
    /// Opens the ClipStudio logs folder in the system file explorer.
    /// Creates the folder first if it does not yet exist.
    /// </summary>
    private static void OpenLogsFolder()
    {
        Directory.CreateDirectory(App.LogsFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName        = App.LogsFolder,
            UseShellExecute = true,
        });
    }

    /// <summary>Opens the GitHub Issues page so the user can submit feedback.</summary>
    private static void SendFeedback()
    {
        // Replace Phazertron/ClipStudio with the real repository path before shipping.
        const string url = "https://github.com/Phazertron/ClipStudio/issues/new";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best-effort.
        }
    }

    // ---- Transcription management ----

    // ---- Helpers ----

    private SourceFolderRowViewModel BuildRow(SourceFolder folder)
        => new(folder, RemoveFolderAsync, ToggleFolderActiveAsync, ScanFolderAsync);
}
