using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;

namespace ClipStudio.Application.Services;

/// <summary>
/// Application service implementation of <see cref="IFilterPresetService"/>.
/// Delegates persistence to <see cref="IFilterPresetRepository"/>.
/// </summary>
public sealed class FilterPresetService : IFilterPresetService
{
    private readonly IFilterPresetRepository _repository;

    /// <summary>Initializes a new instance of <see cref="FilterPresetService"/>.</summary>
    public FilterPresetService(IFilterPresetRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FilterPreset>> GetAllAsync(CancellationToken cancellationToken = default)
        => _repository.GetAllAsync(cancellationToken);

    /// <inheritdoc/>
    public Task SaveAsync(FilterPreset preset, CancellationToken cancellationToken = default)
        => _repository.AddAsync(preset, cancellationToken);

    /// <inheritdoc/>
    public Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        => _repository.DeleteAsync(id, cancellationToken);
}
