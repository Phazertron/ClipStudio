using ClipStudio.Application.Models;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Computes aggregated statistics for the clip library.
/// </summary>
public interface IStatsService
{
    /// <summary>
    /// Asynchronously computes and returns library statistics from the current clip database.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A snapshot of <see cref="LibraryStatistics"/> for the current state of the library.</returns>
    Task<LibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}
