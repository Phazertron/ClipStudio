using ClipStudio.Core.Entities;

namespace ClipStudio.Core.Interfaces;

/// <summary>
/// Defines data access operations for <see cref="FilterPreset"/> entities.
/// </summary>
public interface IFilterPresetRepository
{
    /// <summary>Returns all saved filter presets, ordered by name.</summary>
    Task<IReadOnlyList<FilterPreset>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the preset with the given identifier, or null if not found.</summary>
    Task<FilterPreset?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds a new filter preset to the repository.</summary>
    Task AddAsync(FilterPreset preset, CancellationToken cancellationToken = default);

    /// <summary>Removes the filter preset with the given identifier.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
