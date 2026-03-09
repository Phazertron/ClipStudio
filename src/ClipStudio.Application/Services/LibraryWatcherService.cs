using ClipStudio.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Watches one or more source folders using <see cref="FileSystemWatcher"/> and raises
/// <see cref="ILibraryWatcherService.FileDetected"/> when a new video file stabilises on disk.
/// A short delay is applied after detection to avoid reading a file that is still being written.
/// </summary>
public sealed class LibraryWatcherService : ILibraryWatcherService
{
    private static readonly TimeSpan StabilisationDelay = TimeSpan.FromSeconds(3);
    private static readonly string[] VideoExtensions =
        ["*.mp4", "*.mkv", "*.mov", "*.webm", "*.avi", "*.flv", "*.ts"];

    private readonly Dictionary<string, (FileSystemWatcher Watcher, int SourceFolderId)> _watchers = [];
    private readonly ILogger<LibraryWatcherService> _logger;

    /// <inheritdoc/>
    public event EventHandler<FileDetectedEventArgs>? FileDetected;

    /// <summary>Initializes a new instance of <see cref="LibraryWatcherService"/>.</summary>
    public LibraryWatcherService(ILogger<LibraryWatcherService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public void StartWatching(string folderPath, int sourceFolderId)
    {
        if (_watchers.ContainsKey(folderPath))
        {
            _logger.LogDebug("Already watching: {Path}", folderPath);
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            _logger.LogWarning("Cannot watch non-existent folder: {Path}", folderPath);
            return;
        }

        var watcher = new FileSystemWatcher(folderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        foreach (var ext in VideoExtensions)
            watcher.Filters.Add(ext);

        watcher.Created += (_, args) => OnFileCreated(args.FullPath, sourceFolderId);
        watcher.Renamed += (_, args) => OnFileCreated(args.FullPath, sourceFolderId);

        _watchers[folderPath] = (watcher, sourceFolderId);
        _logger.LogInformation("Watching folder: {Path}", folderPath);
    }

    /// <inheritdoc/>
    public void StopWatching(string folderPath)
    {
        if (!_watchers.TryGetValue(folderPath, out var entry))
            return;

        entry.Watcher.EnableRaisingEvents = false;
        entry.Watcher.Dispose();
        _watchers.Remove(folderPath);
        _logger.LogInformation("Stopped watching: {Path}", folderPath);
    }

    /// <inheritdoc/>
    public void StopAll()
    {
        foreach (var key in _watchers.Keys.ToList())
            StopWatching(key);
    }

    private void OnFileCreated(string filePath, int sourceFolderId)
    {
        // Fire-and-forget with delay to allow the file to finish being written.
        _ = Task.Run(async () =>
        {
            await Task.Delay(StabilisationDelay);

            if (!File.Exists(filePath))
                return;

            _logger.LogInformation("New file detected: {FilePath}", filePath);
            FileDetected?.Invoke(this, new FileDetectedEventArgs(filePath, sourceFolderId));
        });
    }

    /// <inheritdoc/>
    public void Dispose() => StopAll();
}
