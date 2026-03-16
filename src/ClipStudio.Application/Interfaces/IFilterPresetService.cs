using ClipStudio.Core.Entities;

namespace ClipStudio.Application.Interfaces;

/// <summary>
/// Application service for managing saved library filter presets.
/// </summary>
public interface IFilterPresetService
{
    /// <summary>Returns all saved filter presets ordered by name.</summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task<IReadOnlyList<FilterPreset>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a new filter preset with the given name and filter criteria.
    /// </summary>
    /// <param name="preset">The preset to persist.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task SaveAsync(FilterPreset preset, CancellationToken cancellationToken = default);

    /// <summary>Permanently removes the preset with the given identifier.</summary>
    /// <param name="id">The identifier of the preset to delete.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
