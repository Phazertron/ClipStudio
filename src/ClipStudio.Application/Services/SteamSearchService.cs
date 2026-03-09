using System.Text.Json.Nodes;
using ClipStudio.Application.Interfaces;
using ClipStudio.Application.Models;
using Microsoft.Extensions.Logging;

namespace ClipStudio.Application.Services;

/// <summary>
/// Game search service backed by the Steam Community autocomplete API.
/// Requires no credentials or API keys. Cover art is served from the Steam public CDN.
/// </summary>
public sealed class SteamSearchService : IGameSearchService, IDisposable
{
    private const string SearchUrlTemplate =
        "https://steamcommunity.com/actions/SearchApps/{0}";

    private const string CoverUrlTemplate =
        "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/library_600x900.jpg";

    private readonly HttpClient _http;
    private readonly ILogger<SteamSearchService> _logger;

    /// <summary>Initializes a new instance of <see cref="SteamSearchService"/>.</summary>
    /// <param name="http">The HTTP client used for Steam API requests.</param>
    /// <param name="logger">Logger instance.</param>
    public SteamSearchService(HttpClient http, ILogger<SteamSearchService> logger)
    {
        _http   = http;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SteamGame>> SearchAsync(
        string query,
        int limit = 15,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        try
        {
            var url      = string.Format(SearchUrlTemplate, Uri.EscapeDataString(query));
            var response = await _http.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json  = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var games = new List<SteamGame>();

            foreach (var node in json!.AsArray())
            {
                if (node is null) continue;

                // The Steam API returns appid as a JSON string, not a number.
                var appIdStr = node["appid"]?.ToString();
                var name     = node["name"]?.GetValue<string>();

                if (!int.TryParse(appIdStr, out var appId) || string.IsNullOrEmpty(name))
                    continue;

                games.Add(new SteamGame
                {
                    AppId    = appId,
                    Name     = name,
                    CoverUrl = string.Format(CoverUrlTemplate, appId)
                });

                if (games.Count >= limit)
                    break;
            }

            _logger.LogDebug("Steam search for '{Query}' returned {Count} results.", query, games.Count);
            return games;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Steam game search failed for query '{Query}'.", query);
            return [];
        }
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();
}
