using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Enums;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Application service for managing user-defined tags, their hierarchy, and Steam game title lookup.
/// </summary>
public sealed class TagService : ITagService
{
    private readonly ITagRepository _tags;
    private readonly IGameSearchService _gameSearch;
    private readonly ILogger<TagService> _logger;

    /// <summary>Initializes a new instance of <see cref="TagService"/>.</summary>
    public TagService(ITagRepository tags, IGameSearchService gameSearch, ILogger<TagService> logger)
    {
        _tags       = tags;
        _gameSearch = gameSearch;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Tag>> GetAllAsync(CancellationToken cancellationToken = default)
        => _tags.GetAllAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Tag>> GetRootsAsync(CancellationToken cancellationToken = default)
        => _tags.GetRootsAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<Tag>> GetByTypeAsync(TagType type, CancellationToken cancellationToken = default)
        => _tags.GetByTypeAsync(type, cancellationToken);

    /// <inheritdoc/>
    public Task<Tag?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _tags.GetByIdAsync(id, cancellationToken);

    /// <inheritdoc/>
    public async Task<Tag> CreateAsync(
        string name,
        string color,
        string? description = null,
        int? parentTagId = null,
        CancellationToken cancellationToken = default)
    {
        var tag = new Tag
        {
            Name        = name.Trim(),
            Color       = color,
            Description = description,
            ParentTagId = parentTagId,
            Type        = TagType.General
        };

        await _tags.AddAsync(tag, cancellationToken);
        _logger.LogInformation("Created tag '{Name}' (Id={Id}).", tag.Name, tag.Id);
        return tag;
    }

    /// <inheritdoc/>
    public async Task<Tag> CreateFromSteamAsync(SteamGame game, CancellationToken cancellationToken = default)
    {
        var tag = new Tag
        {
            Name           = game.Name,
            Color          = "#1565C0",
            Type           = TagType.Game,
            GameStoreAppId = game.AppId,
            GameCoverUrl   = game.CoverUrl
        };

        await _tags.AddAsync(tag, cancellationToken);
        _logger.LogInformation("Created Game tag '{Name}' from Steam (AppId={AppId}).", tag.Name, game.AppId);
        return tag;
    }

    /// <inheritdoc/>
    public async Task<Tag> CreateCustomGameTagAsync(string name, CancellationToken cancellationToken = default)
    {
        var tag = new Tag
        {
            Name  = name.Trim(),
            Color = "#1565C0",
            Type  = TagType.Game
        };

        await _tags.AddAsync(tag, cancellationToken);
        _logger.LogInformation("Created custom Game tag '{Name}'.", tag.Name);
        return tag;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(
        int tagId,
        string name,
        string color,
        string? description,
        int? parentTagId,
        CancellationToken cancellationToken = default)
    {
        var tag = await RequireTagAsync(tagId, cancellationToken);
        tag.Name        = name.Trim();
        tag.Color       = color;
        tag.Description = description;
        tag.ParentTagId = parentTagId;
        await _tags.UpdateAsync(tag, cancellationToken);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(int tagId, CancellationToken cancellationToken = default)
        => _tags.DeleteAsync(tagId, cancellationToken);

    /// <inheritdoc/>
    public Task AddRelationAsync(int tagId, int relatedTagId, CancellationToken cancellationToken = default)
        => _tags.AddRelationAsync(tagId, relatedTagId, cancellationToken);

    /// <inheritdoc/>
    public Task RemoveRelationAsync(int tagId, int relatedTagId, CancellationToken cancellationToken = default)
        => _tags.RemoveRelationAsync(tagId, relatedTagId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<SteamGame>> SearchGamesAsync(string query, CancellationToken cancellationToken = default)
        => _gameSearch.SearchAsync(query, cancellationToken: cancellationToken);

    private async Task<Tag> RequireTagAsync(int tagId, CancellationToken cancellationToken)
    {
        var tag = await _tags.GetByIdAsync(tagId, cancellationToken);
        if (tag is null)
            throw new InvalidOperationException($"Tag with id {tagId} was not found.");
        return tag;
    }
}
