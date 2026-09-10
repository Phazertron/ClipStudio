using System.Diagnostics;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Application.Parsing;
using ClipStudio.Core.Interfaces;
using ClipStudio.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Compares each source folder's contents against the clip rows that point into it, using one
/// directory listing per folder.
/// </summary>
/// <remarks>
/// Registered as a singleton so <see cref="LastReport"/> survives the scope a check runs in, and
/// can be read later by whatever presents the findings. The repositories it needs are scoped, so
/// each run opens its own scope.
/// </remarks>
public sealed class LibraryHealthCheckService : ILibraryHealthCheckService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<LibraryHealthCheckService> _logger;

    /// <inheritdoc/>
    public LibraryHealthReport? LastReport { get; private set; }

    /// <summary>Initialises a new <see cref="LibraryHealthCheckService"/>.</summary>
    /// <param name="scopeFactory">Creates the scope the run's repositories are resolved from.</param>
    /// <param name="fileSystem">The file system abstraction, so the check is testable without a disk.</param>
    /// <param name="logger">The logger.</param>
    public LibraryHealthCheckService(
        IServiceScopeFactory scopeFactory,
        IFileSystem fileSystem,
        ILogger<LibraryHealthCheckService> logger)
    {
        _scopeFactory = scopeFactory;
        _fileSystem   = fileSystem;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public async Task<LibraryHealthReport> CheckAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        using var scope = _scopeFactory.CreateScope();
        var clips   = scope.ServiceProvider.GetRequiredService<IClipRepository>();
        var folders = scope.ServiceProvider.GetRequiredService<ISourceFolderRepository>();

        var allClips    = await clips.GetFileSnapshotsAsync(ct);
        var allFolders  = await folders.GetAllAsync(ct);
        var findings    = new List<LibraryHealthFinding>();

        var markedBroken = 0;
        var flagsCleared = 0;
        var skipped      = 0;
        var checkedCount = 0;

        var clipsByFolder = allClips
            .GroupBy(c => c.SourceFolderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var folder in allFolders)
        {
            ct.ThrowIfCancellationRequested();

            var folderClips = clipsByFolder.TryGetValue(folder.Id, out var list)
                ? list
                : [];

            if (!_fileSystem.DirectoryExists(folder.Path))
            {
                // Out of reach is not the same as gone. Leaving IsBroken alone here is the whole
                // point: a library on an unplugged drive must come back intact when it is plugged
                // back in, not as several hundred clips the user has to un-break by hand.
                skipped += folderClips.Count;
                findings.Add(new LibraryHealthFinding(
                    LibraryHealthFindingKind.SourceFolderUnreachable,
                    folderClips.Count == 1
                        ? $"Source folder '{folder.Path}' cannot be reached. 1 clip is unavailable."
                        : $"Source folder '{folder.Path}' cannot be reached. {folderClips.Count} clips are unavailable.",
                    SourceFolderId: folder.Id,
                    Path: folder.Path,
                    Count: folderClips.Count));

                _logger.LogWarning(
                    "Source folder {Path} is unreachable; {Count} clip(s) left untouched.",
                    folder.Path, folderClips.Count);
                progress?.Report($"Source folder unavailable: {folder.Path}");
                continue;
            }

            // One listing per folder answers both questions below: which rows have lost their
            // file, and which files have no row.
            var onDisk = new HashSet<string>(
                _fileSystem.EnumerateFiles(folder.Path).Where(ClipFileNameParser.IsSupportedVideoFile),
                StringComparer.OrdinalIgnoreCase);

            var known = new HashSet<string>(
                folderClips.Select(c => c.FilePath),
                StringComparer.OrdinalIgnoreCase);

            foreach (var clip in folderClips)
            {
                ct.ThrowIfCancellationRequested();
                checkedCount++;

                var present = onDisk.Contains(clip.FilePath);

                if (!present && !clip.IsBroken)
                {
                    await clips.SetBrokenAsync(clip.Id, true, ct);
                    markedBroken++;
                    _logger.LogWarning(
                        "Clip {Id} marked broken: source file not found at '{Path}'.", clip.Id, clip.FilePath);
                }
                else if (present && clip.IsBroken)
                {
                    await clips.SetBrokenAsync(clip.Id, false, ct);
                    flagsCleared++;
                    _logger.LogInformation(
                        "Clip {Id} is no longer broken: source file found at '{Path}'.", clip.Id, clip.FilePath);
                }

                if (!present)
                {
                    findings.Add(new LibraryHealthFinding(
                        LibraryHealthFindingKind.ClipFileMissing,
                        $"'{clip.FileName}' is no longer at {clip.FilePath}.",
                        SourceFolderId: folder.Id,
                        ClipId: clip.Id,
                        Path: clip.FilePath));
                }
            }

            var unimported = onDisk.Count(f => !known.Contains(f));
            if (unimported > 0)
            {
                findings.Add(new LibraryHealthFinding(
                    LibraryHealthFindingKind.UnimportedFilesFound,
                    unimported == 1
                        ? $"1 file in '{folder.Path}' has not been imported. A scan is recommended."
                        : $"{unimported} files in '{folder.Path}' have not been imported. A scan is recommended.",
                    SourceFolderId: folder.Id,
                    Path: folder.Path,
                    Count: unimported));
            }

            progress?.Report($"Checked {folder.Path}.");
        }

        // A clip always belongs to a folder row - the foreign key is RESTRICT - but if that ever
        // stopped being true the count would quietly under-report, so say so rather than hide it.
        var knownFolderIds = new HashSet<int>(allFolders.Select(f => f.Id));
        var strays         = allClips.Count(c => !knownFolderIds.Contains(c.SourceFolderId));
        if (strays > 0)
        {
            checkedCount += strays;
            _logger.LogWarning("{Count} clip(s) point at a source folder that no longer exists.", strays);
        }

        stopwatch.Stop();

        var report = new LibraryHealthReport
        {
            Findings                  = findings,
            ClipsMarkedBroken         = markedBroken,
            BrokenFlagsCleared        = flagsCleared,
            ClipsChecked              = checkedCount,
            ClipsSkippedAsUnreachable = skipped,
            Elapsed                   = stopwatch.Elapsed,
        };

        LastReport = report;
        _logger.LogInformation("{Summary}", report.ToString());
        progress?.Report(report.ToString());

        return report;
    }
}
