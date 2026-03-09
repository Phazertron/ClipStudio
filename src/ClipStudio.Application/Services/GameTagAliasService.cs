using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Application service for managing game-tag alias mappings.
/// </summary>
public sealed class GameTagAliasService : IGameTagAliasService
{
    private readonly IGameTagAliasRepository _aliases;
    private readonly ILogger<GameTagAliasService> _logger;

    /// <summary>Initializes a new instance of <see cref="GameTagAliasService"/>.</summary>
    public GameTagAliasService(IGameTagAliasRepository aliases, ILogger<GameTagAliasService> logger)
    {
        _aliases = aliases;
        _logger  = logger;
    }

    /// <inheritdoc/>
    public Task<GameTagAlias?> FindByAliasAsync(string aliasString, CancellationToken cancellationToken = default)
        => _aliases.FindByAliasAsync(aliasString, cancellationToken);

    /// <inheritdoc/>
    public async Task<GameTagAlias> EnsureAliasAsync(string aliasString, int tagId, CancellationToken cancellationToken = default)
    {
        var existing = await _aliases.FindByAliasAsync(aliasString, cancellationToken);
        if (existing is not null)
        {
            _logger.LogDebug("Game alias '{Alias}' already maps to tag {TagId}.", aliasString, existing.TagId);
            return existing;
        }

        var alias = new GameTagAlias { AliasString = aliasString, TagId = tagId };
        await _aliases.AddAsync(alias, cancellationToken);
        _logger.LogInformation("Game alias '{Alias}' → tag {TagId} created.", aliasString, tagId);
        return alias;
    }
}
