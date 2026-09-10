using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Checks, cheaply, whether the library still matches what is on disk.
/// </summary>
/// <remarks>
/// This is the half of the old startup sanitize that is worth running on every launch. The full
/// pass - hash backfill, thumbnail and strip regeneration, cache sweeps, the timestamp heuristic -
/// stays in <see cref="ILibrarySanitizerService"/> and runs only when the user asks for a repair.
/// <para>
/// The split is affordable because checking the disk is nearly free and the regeneration is not:
/// one directory listing of 885 files measures under a millisecond, while the full pass costs
/// roughly 10 ms per clip and grows with the library. What the startup pass used to provide
/// silently - regenerating a thumbnail that had gone missing - is now done on demand by
/// <see cref="IMediaAssetProvider"/> when something actually asks for the image.
/// </para>
/// </remarks>
public interface ILibraryHealthCheckService
{
    /// <summary>
    /// Gets the most recent report, or <see langword="null"/> when no check has run yet.
    /// </summary>
    LibraryHealthReport? LastReport { get; }

    /// <summary>
    /// Compares each reachable source folder's contents against the clip rows that point into it.
    /// </summary>
    /// <param name="progress">Optional progress sink; receives one message per source folder.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>What was found, and what the check changed.</returns>
    /// <remarks>
    /// The only writes are to <c>Clip.IsBroken</c>, set when a reachable folder no longer holds the
    /// file and cleared when it does again. Clips in an unreachable folder are left exactly as they
    /// are: a disconnected drive is not the same as a deleted file, and marking a whole library
    /// broken because a drive was unplugged is the behaviour this check exists to avoid.
    /// </remarks>
    Task<LibraryHealthReport> CheckAsync(
        IProgress<string>? progress = null, CancellationToken ct = default);
}
